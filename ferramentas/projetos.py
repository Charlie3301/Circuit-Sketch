"""python ferramentas/projetos.py [pasta-de-saida]

Os projetos de teste da Alpha 24: circuitos inteiros, do jeito que se
montam de verdade, para saber como cada coisa se comporta no simulador.

  - os veiculos: o drone (ESC 4-em-1, helices, radio, giroscopio), o
    carrinho de controle remoto (sem placa e com Arduino), o rover 4WD e o
    rover 4WD com o HC-SR04 (o carrinho classico dos kits);
  - os reguladores de tensao (7805, AMS1117, LM2596, MT3608) alimentando a
    placa sem o cabo USB;
  - os projetos classicos de quem comeca: semaforo, sensor de re,
    termometro, alarme, fechadura, controle IR, display, motor de passo...

Cada projeto sai em <pasta>/<nome>.sktch, e o roteiro do teste dele em
<pasta>/roteiros.json: o que mexer (o manche, a distancia, o botao) e o
que conferir (o motor girando, o servo, o LED, o que sai no monitor). O
bloco Projetos_Testados do autoteste roda os roteiros; o
ferramentas/testados.py separa os projetos em WORKING e NOT WORKING.

Os dois projetos do rover com HC-SR04 usam o codigo do usuario (as
pastas Rover_ponte_H e Uno_DesvioObstaculos dos Downloads), se estiverem la.
"""
import io
import json
import os
import sys

BASE = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..')).replace('\\', '/') + '/'
SAIDA = sys.argv[1] if len(sys.argv) > 1 else BASE + 'projetos'
DOWNLOADS = 'C:/Users/seraf/Downloads/'
MM = 96 / 25.4

VERMELHO, PRETO, VERDE, AZUL, AMARELO, LARANJA, ROXO, BRANCO, CINZA, MARROM = (
    '#D23B33', '#1A1A1A', '#00A651', '#1F6FD1', '#E8B400', '#F07F1A', '#8E44AD', '#F2F2F2', '#8A8F98', '#7A4A2A')

PROJETOS = {}


def fio(a, pa, b, pb, cor=PRETO):
    return {'DePeca': a, 'DePino': pa, 'ParaPeca': b, 'ParaPino': pb, 'Cor': cor}


def peca(id_, tipo, x, y, **extra):
    return {'Id': id_, 'Tipo': tipo, 'X': round(x, 2), 'Y': round(y, 2), **extra}


def bateria(id_, volts, corrente_ma, capacidade_mah, nome, x, y):
    """A corrente maxima que ela entrega (mA) e a carga dela (mAh)."""
    return peca(id_, 'bateria', x, y, Volts=volts, MiliAmperes=corrente_ma, MiliAmperesHora=capacidade_mah, Nome=nome)


EIXO = {'motor-tt': (12, 13), 'motor-dc': (14, 12.5), 'motor-brushless': (18, 18)}
CENTRO = {'roda-tt': (33, 33), 'helice-130': (38, 38), 'helice-1045': (128, 17), 'helice-5045': (64, 64)}
LARGURA_MM = {'motor-tt': 280 / MM, 'motor-dc': 106 / MM, 'motor-brushless': 136 / MM}


def no_eixo(acessorio, motor, x, y, espelhado=False):
    """O acessorio (roda, helice) com o centro no eixo do motor que esta em x, y."""
    ex, ey = EIXO[motor]
    if espelhado:
        ex = LARGURA_MM[motor] - ex
    cx, cy = CENTRO[acessorio]
    return (x + (ex - cx) * MM, y + (ey - cy) * MM)


def led(id_, cor, x, y, pino_placa, gnd_placa, ohms=220, placa='placa', cor_fio=VERDE):
    """LED com resistor: o pino -> resistor -> anodo; catodo -> GND."""
    pecas = [peca(id_, 'led', x, y, Cor=cor), peca('r_' + id_, 'resistor', x - 70, y + 60, Ohms=ohms)]
    fios = [fio(placa, pino_placa, 'r_' + id_, '1', cor_fio),
            fio('r_' + id_, '2', id_, 'A', cor_fio),
            fio(id_, 'C', placa, gnd_placa, PRETO)]
    return pecas, fios


def botao(id_, x, y, pino_placa, gnd_placa, placa='placa'):
    """Botao para o GND (o sketch usa INPUT_PULLUP: apertado le LOW)."""
    return [peca(id_, 'pushbutton', x, y)], [fio(placa, pino_placa, id_, '1.l', AZUL), fio(id_, '2.l', placa, gnd_placa, PRETO)]


# ---- o roteiro do teste ----

def esperar(s):
    return {'esperar': s}


def mexer(id_, **campos):
    """Muda um controle da peca: giro (a distancia do sonar, o manche...), giro2..4, bits, volts."""
    return {'peca': id_, **campos}


def apertar(id_):
    return {'apertar': id_}


def soltar(id_):
    return {'soltar': id_}


def clicar(id_, ms=0.3):
    return [apertar(id_), esperar(ms), soltar(id_), esperar(0.25)]


def tecla(id_, t):
    return {'tecla': id_, 'valor': t}


def confere(nome, ate=3.0, **teste):
    return {'confere': {'nome': nome, **teste}, 'ate': ate}


def projeto(nome, titulo, descricao, codigo, pecas, fios, roteiro, outros_codigos=None):
    PROJETOS[nome] = {
        'titulo': titulo,
        'descricao': [titulo, ''] + descricao,
        'codigo': codigo,
        'pecas': pecas,
        'fios': fios,
        'roteiro': [p for passo in roteiro for p in (passo if isinstance(passo, list) else [passo])],
        'outros': outros_codigos or {},
    }


SEM_PLACA = [
    '// Sem placa: o receptor manda direto no ESC e no servo, como no',
    '// carrinho de controle remoto de verdade. Nao ha nada para rodar.',
]


# =====================================================================
#  1) O DRONE: ESP32 de controladora, ESC 4-em-1, quatro 2205 2300KV
#     com helices 5045, receptor por iBUS, MPU6050 e LiPo 4S
# =====================================================================

def drone():
    pecas, fios = [], []
    # O quadro "X": a frente e para cima. M1 traseiro direito, M2 dianteiro
    # direito, M3 traseiro esquerdo, M4 dianteiro esquerdo (a ordem do Betaflight).
    cantos = {'m1': (1000, 1000), 'm2': (1000, 100), 'm3': (100, 1000), 'm4': (100, 100)}
    for m, (x, y) in cantos.items():
        pecas.append(peca(m, 'motor-brushless', x, y, Modelo='2205-2300'))
        hx, hy = no_eixo('helice-5045', 'motor-brushless', x, y)
        pecas.append(peca('h' + m[1], 'helice-5045', hx, hy))
    pecas += [
        peca('placa', 'esp32-devkit-v1', 360, 380),
        peca('rx', 'receptor-rc', 560, 350),
        peca('esc', 'esc-4em1', 520, 490),
        peca('mpu', 'mpu6050', 370, 610),
        peca('reg', 'lm2596', 520, 660, Volts=5.0),
        bateria('lipo', 14.8, 120000, 1500, 'LiPo 4S 1500 mAh (14,8 V)', 360, 700),
        peca('controle', 'radio-controle', 1300, 420, Giro=0.0, Giro2=0.5, Giro3=0.5, Giro4=0.5),
    ]
    fios += [
        fio('lipo', '+', 'esc', 'BAT+', VERMELHO), fio('lipo', '-', 'esc', 'BAT-', PRETO),
        fio('lipo', '+', 'reg', 'IN+', VERMELHO), fio('lipo', '-', 'reg', 'IN-', PRETO),
        fio('reg', 'OUT+', 'placa', 'VIN', VERMELHO), fio('reg', 'OUT-', 'placa', 'GND.2', PRETO),
        fio('reg', 'OUT+', 'rx', 'VCC', VERMELHO), fio('reg', 'OUT-', 'rx', 'GND', PRETO),
        fio('rx', 'IBUS', 'placa', 'RX2', AMARELO),
        fio('esc', 'GND', 'placa', 'GND.2', PRETO),
        fio('placa', 'D13', 'esc', 'S1', AMARELO), fio('placa', 'D12', 'esc', 'S2', VERDE),
        fio('placa', 'D14', 'esc', 'S3', AZUL), fio('placa', 'D27', 'esc', 'S4', ROXO),
        fio('mpu', 'VCC', 'placa', '3V3', VERMELHO), fio('mpu', 'GND', 'placa', 'GND.1', PRETO),
        fio('mpu', 'SDA', 'placa', 'D21', AZUL), fio('mpu', 'SCL', 'placa', 'D22', AMARELO),
    ]
    for k, m in enumerate(['m1', 'm2', 'm3', 'm4'], start=1):
        for fase, cor in zip('ABC', [AZUL, AMARELO, VERMELHO]):
            fios.append(fio('esc', f'M{k}{fase}', m, fase, cor))

    codigo = [
        '// Drone quadricoptero: a ESP32 e a controladora de voo.',
        '//',
        '// O receptor FS-iA6B manda os 14 canais por iBUS no RX2 (GPIO16).',
        '// O MPU6050 mede a rotacao (o giroscopio); a controladora compara',
        '// com o que o manche pede e corrige cada motor (o "modo acro").',
        '// A ESC 4-em-1 recebe um pulso de servo por motor (1000 a 2000 us).',
        '//',
        '// Armar: acelerador embaixo e leme todo para a DIREITA por 1 s.',
        '// Desarmar: acelerador embaixo e leme todo para a ESQUERDA por 1 s.',
        '// Sem sinal do radio por meio segundo: desarma (failsafe).',
        '',
        '#include <Wire.h>',
        '#include <Adafruit_MPU6050.h>',
        '#include <ESP32Servo.h>',
        '',
        'Adafruit_MPU6050 mpu;',
        'Servo motor[4];',
        'const int PINOS[4] = {13, 12, 14, 27};   // M1 traseiro dir., M2 dianteiro dir., M3 traseiro esq., M4 dianteiro esq.',
        '',
        'uint8_t quadro[32];',
        'int posicao = 0;',
        'int canal[14];',
        'unsigned long ultimoQuadro = 0;',
        '',
        'bool armado = false;',
        'int gesto = 0;',
        'unsigned long desdeGesto = 0;',
        'unsigned long ultimoRelato = 0;',
        'const float GANHO = 0.8;   // o P do controle de rotacao',
        '',
        '// Um quadro iBUS: 0x20 0x40, 14 canais de 16 bits e a soma de conferencia.',
        'void lerIbus() {',
        '  while (Serial2.available()) {',
        '    uint8_t b = Serial2.read();',
        '    if (posicao == 0 && b != 0x20) continue;',
        '    if (posicao == 1 && b != 0x40) { posicao = 0; continue; }',
        '    quadro[posicao++] = b;',
        '    if (posicao < 32) continue;',
        '    posicao = 0;',
        '    uint16_t soma = 0xFFFF;',
        '    for (int i = 0; i < 30; i++) soma -= quadro[i];',
        '    if (soma != (quadro[30] | (quadro[31] << 8))) continue;',
        '    for (int c = 0; c < 14; c++) canal[c] = quadro[2 + c * 2] | (quadro[3 + c * 2] << 8);',
        '    ultimoQuadro = millis();',
        '  }',
        '}',
        '',
        'void todos(int us) {',
        '  for (int i = 0; i < 4; i++) motor[i].writeMicroseconds(us);',
        '}',
        '',
        'void setup() {',
        '  Serial.begin(115200);',
        '  Serial2.begin(115200, SERIAL_8N1, 16, 17);',
        '  for (int i = 0; i < 4; i++) motor[i].attach(PINOS[i], 1000, 2000);',
        '  todos(1000);   // a ESC so arma com o acelerador no minimo',
        '  if (!mpu.begin()) Serial.println("MPU6050 nao encontrado!");',
        '  mpu.setGyroRange(MPU6050_RANGE_500_DEG);',
        '  Serial.println("Drone pronto: acelerador embaixo e leme para a direita para armar");',
        '}',
        '',
        'void loop() {',
        '  lerIbus();',
        '  bool comSinal = millis() - ultimoQuadro < 500;',
        '  int aileron = canal[0], profundor = canal[1], acelerador = canal[2], leme = canal[3];',
        '',
        '  if (!comSinal) {',
        '    if (armado) Serial.println("SEM SINAL do radio: desarmado (failsafe)");',
        '    armado = false;',
        '    gesto = 0;',
        '  } else if (acelerador < 1050 && leme > 1900) {',
        '    if (gesto != 1) { gesto = 1; desdeGesto = millis(); }',
        '    if (!armado && millis() - desdeGesto > 1000) { armado = true; Serial.println("ARMADO"); }',
        '  } else if (acelerador < 1050 && leme < 1100) {',
        '    if (gesto != 2) { gesto = 2; desdeGesto = millis(); }',
        '    if (armado && millis() - desdeGesto > 1000) { armado = false; Serial.println("DESARMADO"); }',
        '  } else {',
        '    gesto = 0;',
        '  }',
        '',
        '  if (!armado) {',
        '    todos(1000);',
        '  } else {',
        '    sensors_event_t a, g, temp;',
        '    mpu.getEvent(&a, &g, &temp);',
        '    float gx = g.gyro.x * 57.3, gy = g.gyro.y * 57.3, gz = g.gyro.z * 57.3;   // graus/s',
        '    float roll = GANHO * ((aileron - 1500) * 0.4 - gx);',
        '    float pitch = GANHO * ((profundor - 1500) * 0.4 - gy);',
        '    float yaw = GANHO * ((leme - 1500) * 0.4 - gz);',
        '    int base = constrain(acelerador, 1100, 1800);',
        '    int m[4];',
        '    m[0] = base - roll + pitch + yaw;   // M1: traseiro direito (anti-horario)',
        '    m[1] = base - roll - pitch - yaw;   // M2: dianteiro direito (horario)',
        '    m[2] = base + roll + pitch - yaw;   // M3: traseiro esquerdo (horario)',
        '    m[3] = base + roll - pitch + yaw;   // M4: dianteiro esquerdo (anti-horario)',
        '    for (int i = 0; i < 4; i++) motor[i].writeMicroseconds(constrain(m[i], 1100, 2000));',
        '    if (millis() - ultimoRelato > 500) {',
        '      ultimoRelato = millis();',
        '      Serial.print("acelerador ");',
        '      Serial.print(acelerador);',
        '      Serial.print("  motores ");',
        '      for (int i = 0; i < 4; i++) { Serial.print(constrain(m[i], 1100, 2000)); Serial.print(" "); }',
        '      Serial.println();',
        '    }',
        '  }',
        '  delay(4);',
        '}',
    ]
    roteiro = [
        mexer('controle', bits=0, giro=0.0, giro2=0.5, giro3=0.5, giro4=0.5),
        confere('a ESP32 liga pelo LM2596 (sem o cabo USB)', 4, energizada=True),
        confere('a controladora iniciou', 4, serial='Drone pronto'),
        esperar(3.5),
        mexer('controle', giro4=1.0),
        confere('arma com o acelerador embaixo e o leme para a direita', 4, serial='ARMADO'),
        mexer('controle', giro4=0.5, giro=0.55),
        confere('com o acelerador em 55% os quatro motores giram', 6, rpm_min=['m1', 'm2', 'm3', 'm4'], valor=5000),
        confere('as quatro helices empurram', 3, empuxo_min=['m1', 'm2', 'm3', 'm4'], valor=50),
        mexer('controle', giro3=0.85),
        confere('aileron para a direita: os motores da esquerda aceleram', 4, rpm_maior=[['m3', 'm1'], ['m4', 'm2']]),
        mexer('controle', giro3=0.5, giro=0.0),
        esperar(0.5),
        mexer('controle', giro4=0.0),
        confere('desarma com o acelerador embaixo e o leme para a esquerda', 4, serial='DESARMADO'),
        mexer('controle', giro4=0.5),
    ]
    projeto('drone-quadricoptero', 'Drone quadricoptero (ESP32, ESC 4-em-1, radio FlySky)', [
        'O drone de 5 polegadas: quatro motores 2205 2300KV com helices 5045 de',
        'tres pas, uma ESC 4-em-1, a LiPo 4S de 1500 mAh e a ESP32 como',
        'controladora de voo, com o MPU6050 medindo a rotacao.',
        '',
        'Energia: a LiPo vai na ESC (os motores) e no LM2596, que baixa para 5 V',
        'e alimenta a ESP32 (pelo VIN) e o receptor -- sem o cabo USB.',
        '',
        'O radio: o receptor FS-iA6B manda os canais por iBUS no RX2. Ligue o',
        'controle (a chave dele), deixe o acelerador embaixo e leve o leme',
        '(manche esquerdo) toda para a direita por 1 s: ARMADO. Suba o',
        'acelerador e as helices giram; o manche direito inclina o drone',
        '(acelera os motores de um lado). Para desarmar: acelerador embaixo e',
        'leme toda para a esquerda.',
        '',
        'O rotulo de cada helice mostra o empuxo, em gramas-forca.',
    ], codigo, pecas, fios, roteiro)


# =====================================================================
#  2) O CARRINHO DE CONTROLE REMOTO: sem placa, e com Arduino no meio
# =====================================================================

def carrinho_rc_sem_placa():
    pecas = [
        peca('controle', 'radio-controle', 760, 20, Giro=0.0, Giro2=0.5, Giro3=0.5, Giro4=0.5),
        peca('rx', 'receptor-rc', 440, 80),
        peca('esc', 'esc', 140, 300, Amperes=60),
        peca('motor', 'motor-brushless', 20, 470, Modelo='3650-3100'),
        peca('direcao', 'servo-mg996r', 470, 330),
        bateria('lipo', 7.4, 100000, 5200, 'LiPo 2S 5200 mAh (7,4 V)', 470, 520),
    ]
    fios = [
        fio('lipo', '+', 'esc', 'BAT+', VERMELHO), fio('lipo', '-', 'esc', 'BAT-', PRETO),
        fio('esc', '5V', 'rx', 'VCC', VERMELHO), fio('esc', 'GND', 'rx', 'GND', PRETO),
        fio('rx', 'CH3', 'esc', 'SINAL', BRANCO),
        fio('rx', 'CH1', 'direcao', 'PWM', LARANJA),
        fio('esc', '5V', 'direcao', 'V+', VERMELHO), fio('esc', 'GND', 'direcao', 'GND', MARROM),
        fio('esc', 'A', 'motor', 'A', AZUL), fio('esc', 'B', 'motor', 'B', AMARELO), fio('esc', 'C', 'motor', 'C', VERMELHO),
    ]
    roteiro = [
        mexer('controle', bits=0, giro=0.0, giro2=0.5, giro3=0.5, giro4=0.5),
        esperar(4.0),
        confere('com o manche no centro as rodas ficam retas (servo em ~90 graus)', 3, servo={'peca': 'direcao', 'min': 80, 'max': 100}),
        mexer('controle', giro3=1.0),
        # O pulso do radio vai de 1000 a 2000 us: no servo, ~39 a ~141 graus (e nao 0 a 180).
        confere('manche direito para a direita: o servo vira (~141 graus, os 2000 us)', 3, servo={'peca': 'direcao', 'min': 130, 'max': 150}),
        mexer('controle', giro3=0.0),
        confere('manche direito para a esquerda: o servo vira (~39 graus, os 1000 us)', 3, servo={'peca': 'direcao', 'min': 30, 'max': 50}),
        mexer('controle', giro3=0.5, giro=0.4),
        confere('o acelerador em 40% gira o motor', 6, rpm_min=['motor'], valor=3000),
        confere('e nada esquenta nem queima', 1, sem_avisos=True),
        mexer('controle', giro=0.0),
    ]
    projeto('carrinho-rc-sem-placa', 'Carrinho de controle remoto (sem placa)', [
        'O carrinho RC de verdade, sem microcontrolador: o receptor FS-iA6B manda',
        'o canal 1 (manche direito, para os lados) no servo da direcao e o',
        'canal 3 (acelerador, manche esquerdo) no ESC do motor brushless 3650',
        '3100KV. O BEC do ESC (5 V) alimenta o receptor e o servo.',
        '',
        'Para usar: ligue o controle com o acelerador embaixo e espere os bipes',
        'do ESC (ele arma). Depois o manche esquerdo acelera e o direito vira.',
        '',
        'O ESC deste simulador e de aeromodelo: so gira para a frente (o de',
        'carrinho tem re).',
    ], SEM_PLACA, pecas, fios, roteiro)


def carrinho_rc_arduino():
    pecas = [
        peca('controle', 'radio-controle', 760, 20, Giro=0.0, Giro2=0.5, Giro3=0.5, Giro4=0.5),
        peca('rx', 'receptor-rc', 440, 60),
        peca('placa', 'arduino-nano', 420, 260),
        peca('esc', 'esc', 100, 360, Amperes=60),
        peca('motor', 'motor-brushless', 10, 520, Modelo='3650-3100'),
        peca('direcao', 'servo-mg996r', 470, 400),
        bateria('lipo', 7.4, 100000, 5200, 'LiPo 2S 5200 mAh (7,4 V)', 470, 580),
    ]
    fios = [
        fio('lipo', '+', 'esc', 'BAT+', VERMELHO), fio('lipo', '-', 'esc', 'BAT-', PRETO),
        fio('esc', '5V', 'placa', '5V', VERMELHO), fio('esc', 'GND', 'placa', 'GND.1', PRETO),
        fio('placa', '5V.2', 'rx', 'VCC', VERMELHO), fio('placa', 'GND.2', 'rx', 'GND', PRETO),
        fio('rx', 'CH1', 'placa', '2', LARANJA), fio('rx', 'CH3', 'placa', '3', BRANCO),
        fio('placa', '9', 'direcao', 'PWM', LARANJA), fio('esc', '5V', 'direcao', 'V+', VERMELHO), fio('esc', 'GND', 'direcao', 'GND', MARROM),
        fio('placa', '10', 'esc', 'SINAL', BRANCO),
        fio('esc', 'A', 'motor', 'A', AZUL), fio('esc', 'B', 'motor', 'B', AMARELO), fio('esc', 'C', 'motor', 'C', VERMELHO),
    ]
    codigo = [
        '// Carrinho RC com um Arduino Nano no meio: ele le o receptor, limita a',
        '// velocidade (modo iniciante: no maximo 70%) e para tudo se o radio sumir.',
        '',
        '#include <Servo.h>',
        '',
        'const int CANAL_DIRECAO = 2;      // CH1 do receptor',
        'const int CANAL_ACELERADOR = 3;   // CH3 do receptor',
        'const int PINO_SERVO = 9;',
        'const int PINO_ESC = 10;',
        'const int LIMITE = 1700;          // 70% do acelerador',
        '',
        'Servo direcao;',
        'Servo esc;',
        'unsigned long ultimoRelato = 0;',
        '',
        'void setup() {',
        '  Serial.begin(9600);',
        '  pinMode(CANAL_DIRECAO, INPUT);',
        '  pinMode(CANAL_ACELERADOR, INPUT);',
        '  direcao.attach(PINO_SERVO);',
        '  esc.attach(PINO_ESC, 1000, 2000);',
        '  esc.writeMicroseconds(1000);   // o ESC arma com o acelerador no minimo',
        '  Serial.println("Carrinho RC com Arduino: ligue o controle");',
        '}',
        '',
        'void loop() {',
        '  unsigned long d = pulseIn(CANAL_DIRECAO, HIGH, 30000);',
        '  unsigned long a = pulseIn(CANAL_ACELERADOR, HIGH, 30000);',
        '',
        '  if (d == 0 || a == 0) {',
        '    // failsafe: sem sinal, motor parado e rodas retas',
        '    esc.writeMicroseconds(1000);',
        '    direcao.write(90);',
        '    Serial.println("sem sinal do radio");',
        '    delay(200);',
        '    return;',
        '  }',
        '',
        '  int angulo = map(constrain(d, 1000, 2000), 1000, 2000, 30, 150);',
        '  direcao.write(angulo);',
        '  esc.writeMicroseconds(constrain(a, 1000, LIMITE));',
        '',
        '  if (millis() - ultimoRelato > 500) {',
        '    ultimoRelato = millis();',
        '    Serial.print("direcao ");',
        '    Serial.print(angulo);',
        '    Serial.print(" graus, acelerador ");',
        '    Serial.print(a);',
        '    Serial.println(" us");',
        '  }',
        '}',
    ]
    roteiro = [
        mexer('controle', bits=0, giro=0.0, giro2=0.5, giro3=0.5, giro4=0.5),
        confere('o Nano liga pelo BEC do ESC (sem o cabo USB)', 4, energizada=True),
        esperar(4.0),
        confere('o Nano le o receptor', 4, serial='acelerador'),
        mexer('controle', giro3=1.0),
        confere('manche para a direita: a direcao vai a ~150 graus', 3, servo={'peca': 'direcao', 'min': 140, 'max': 160}),
        mexer('controle', giro3=0.5, giro=0.6),
        confere('o acelerador gira o motor (limitado a 70%)', 6, rpm_min=['motor'], valor=3000),
        mexer('controle', bits=4),
        confere('controle desligado: o Nano entra no failsafe', 4, serial='sem sinal do radio'),
        confere('e para o motor', 8, rpm_max=['motor'], valor=500),
    ]
    projeto('carrinho-rc-arduino', 'Carrinho de controle remoto com Arduino Nano', [
        'O carrinho RC com um Arduino Nano entre o receptor e os motores: ele le',
        'os pulsos do receptor (pulseIn), limita a velocidade a 70% (o "modo',
        'iniciante" dos carrinhos de loja) e, sem sinal do radio, para o motor',
        'e endireita as rodas (o failsafe).',
        '',
        'Energia: a LiPo 2S vai no ESC, e o BEC dele (5 V) alimenta o Nano, o',
        'receptor e o servo -- sem o cabo USB.',
        '',
        'Para usar: ligue o controle com o acelerador embaixo e espere o ESC',
        'armar. Desligue o controle com o carrinho andando: ele para.',
    ], codigo, pecas, fios, roteiro)


# =====================================================================
#  3) O ROVER 4WD (L298N, quatro motores amarelos, 2 x 18650)
# =====================================================================

def chassi_4wd(com_frente_livre=False):
    """Os quatro motores com as rodas (dois por lado), a ponte H e as baterias."""
    pecas, fios = [], []
    y_frente = 260 if com_frente_livre else 120
    y_tras = y_frente + 500
    motores = {
        'm_fe': (180, y_frente, False), 'm_te': (180, y_tras, False),
        'm_fd': (760, y_frente, True), 'm_td': (760, y_tras, True),
    }
    for m, (x, y, espelhado) in motores.items():
        pecas.append(peca(m, 'motor-tt', x, y, Espelhada=espelhado))
        rx, ry = no_eixo('roda-tt', 'motor-tt', x, y, espelhado)
        pecas.append(peca('roda' + m[1:], 'roda-tt', rx, ry))
    pecas += [
        peca('ponte', 'l298n', 500, y_frente + 400),
        bateria('baterias', 7.4, 5000, 2600, '2 x 18650 (7,4 V)', 520, y_frente + 600),
    ]
    fios += [
        fio('baterias', '+', 'ponte', '12V', VERMELHO), fio('baterias', '-', 'ponte', 'GND', PRETO),
        # lado esquerdo em paralelo no OUT1/OUT2, lado direito no OUT3/OUT4
        fio('ponte', 'OUT1', 'm_fe', '+', VERMELHO), fio('ponte', 'OUT2', 'm_fe', '-', PRETO),
        fio('ponte', 'OUT1', 'm_te', '+', VERMELHO), fio('ponte', 'OUT2', 'm_te', '-', PRETO),
        fio('ponte', 'OUT3', 'm_fd', '+', VERMELHO), fio('ponte', 'OUT4', 'm_fd', '-', PRETO),
        fio('ponte', 'OUT3', 'm_td', '+', VERMELHO), fio('ponte', 'OUT4', 'm_td', '-', PRETO),
    ]
    return pecas, fios, y_frente


def rover_4wd():
    pecas, fios, yf = chassi_4wd()
    pecas.append(peca('placa', 'arduino-uno', 470, yf + 180))
    fios += [
        fio('ponte', '5V', 'placa', '5V', VERMELHO), fio('ponte', 'GND', 'placa', 'GND.2', PRETO),
        fio('placa', '5', 'ponte', 'ENA', AMARELO), fio('placa', '7', 'ponte', 'IN1', VERDE), fio('placa', '8', 'ponte', 'IN2', AZUL),
        fio('placa', '9', 'ponte', 'IN3', VERDE), fio('placa', '11', 'ponte', 'IN4', AZUL), fio('placa', '6', 'ponte', 'ENB', AMARELO),
    ]
    codigo = [
        '// Rover 4WD: a ponte H L298N com os dois motores de cada lado em paralelo.',
        '// ENA e ENB nos pinos 5 e 6 (com PWM): a velocidade de cada lado.',
        '',
        'const int ENA = 5;    // lado esquerdo',
        'const int IN1 = 7;',
        'const int IN2 = 8;',
        'const int IN3 = 9;',
        'const int IN4 = 11;',
        'const int ENB = 6;    // lado direito',
        'const int VELOCIDADE = 200;',
        '',
        'void lado(int en, int a, int b, int sentido) {',
        '  digitalWrite(a, sentido > 0 ? HIGH : LOW);',
        '  digitalWrite(b, sentido < 0 ? HIGH : LOW);',
        '  analogWrite(en, sentido == 0 ? 0 : VELOCIDADE);',
        '}',
        '',
        'void mover(int esquerdo, int direito, String nome) {',
        '  lado(ENA, IN1, IN2, esquerdo);',
        '  lado(ENB, IN3, IN4, direito);',
        '  Serial.println(nome);',
        '}',
        '',
        'void setup() {',
        '  Serial.begin(9600);',
        '  pinMode(ENA, OUTPUT); pinMode(IN1, OUTPUT); pinMode(IN2, OUTPUT);',
        '  pinMode(ENB, OUTPUT); pinMode(IN3, OUTPUT); pinMode(IN4, OUTPUT);',
        '  mover(0, 0, "parado");',
        '  delay(500);',
        '}',
        '',
        'void loop() {',
        '  mover(1, 1, "frente");',
        '  delay(2000);',
        '  mover(0, 0, "parado");',
        '  delay(500);',
        '  mover(-1, -1, "re");',
        '  delay(1500);',
        '  mover(-1, 1, "girando para a esquerda");',
        '  delay(1000);',
        '  mover(1, -1, "girando para a direita");',
        '  delay(1000);',
        '  mover(0, 0, "parado");',
        '  delay(1000);',
        '}',
    ]
    todos = ['m_fe', 'm_te', 'm_fd', 'm_td']
    roteiro = [
        confere('o Uno liga pelo 5 V da ponte H (sem o cabo USB)', 4, energizada=True),
        confere('frente', 4, serial='frente'),
        confere('para a frente os quatro motores giram no mesmo sentido', 2.5,
                rpm_sinal=[{'peca': m, 'sinal': 1, 'min': 40} for m in todos]),
        confere('re', 5, linha='re'),
        confere('de re os quatro invertem', 1.5, rpm_sinal=[{'peca': m, 'sinal': -1, 'min': 40} for m in todos]),
        confere('girando para a esquerda', 3, serial='girando para a esquerda'),
        confere('no giro, o lado esquerdo vai para tras e o direito para a frente', 1.0,
                rpm_sinal=[{'peca': 'm_fe', 'sinal': -1, 'min': 20}, {'peca': 'm_fd', 'sinal': 1, 'min': 20}]),
        confere('e a ponte H nao reclama', 1, sem_avisos=True),
    ]
    projeto('rover-4wd', 'Rover 4WD (L298N, 4 motores amarelos)', [
        'O chassi 4WD dos kits: quatro motores amarelos com rodas de 65 mm, os',
        'dois de cada lado ligados em paralelo na ponte H L298N, e duas 18650',
        '(7,4 V) alimentando tudo.',
        '',
        'Energia: as baterias vao no 12V da ponte H; o regulador dela (com 7 V',
        'ou mais na entrada) faz os 5 V que ligam o Uno pelo pino 5V -- sem o',
        'cabo USB.',
        '',
        'O sketch anda para a frente, da re e gira no proprio eixo para os dois',
        'lados, em ciclo. O rotulo de cada roda mostra a velocidade.',
    ], codigo, pecas, fios, roteiro)


def codigo_do_usuario(caminho):
    if not os.path.exists(caminho):
        return None
    return io.open(caminho, encoding='utf-8').read().replace('\r\n', '\n').split('\n')


def rover_4wd_hc_sr04():
    codigo = codigo_do_usuario(DOWNLOADS + 'Rover_ponte_H/Rover_ponte_H.ino')
    if codigo is None:
        print('rover-4wd-hc-sr04: Rover_ponte_H.ino nao esta nos Downloads; pulando')
        return
    pecas, fios, yf = chassi_4wd(com_frente_livre=True)
    pecas += [
        peca('placa', 'arduino-uno', 470, yf + 180),
        peca('servo', 'servo', 520, 20),
        peca('sonar', 'hc-sr04', 520, 150, Giro=0.25),
    ]
    fios += [
        fio('ponte', '5V', 'placa', '5V', VERMELHO), fio('ponte', 'GND', 'placa', 'GND.2', PRETO),
        fio('placa', '2', 'ponte', 'ENA', AMARELO), fio('placa', '4', 'ponte', 'IN1', VERDE), fio('placa', '5', 'ponte', 'IN2', AZUL),
        fio('placa', '6', 'ponte', 'IN3', VERDE), fio('placa', '7', 'ponte', 'IN4', AZUL), fio('placa', '3', 'ponte', 'ENB', AMARELO),
        fio('placa', '8', 'sonar', 'TRIG', VERDE), fio('placa', '9', 'sonar', 'ECHO', AZUL),
        fio('placa', '5V', 'sonar', 'VCC', VERMELHO), fio('placa', 'GND.3', 'sonar', 'GND', PRETO),
        fio('placa', '10', 'servo', 'PWM', LARANJA), fio('placa', '5V', 'servo', 'V+', VERMELHO), fio('placa', 'GND.1', 'servo', 'GND', MARROM),
    ]
    todos = ['m_fe', 'm_te', 'm_fd', 'm_td']
    roteiro = [
        confere('o Uno liga pelo 5 V da ponte H (sem o cabo USB)', 4, energizada=True),
        # A conta do sketch (0,034 cm/us) com o som a 343 m/s: 99 cm.
        confere('com 100 cm livres o sonar le ~100 cm (99, pela conta do sketch)', 6, serial='Frente: 99'),
        confere('e o rover anda para a frente (os quatro motores)', 3, rpm_sinal=[{'peca': m, 'sinal': 1, 'min': 40} for m in todos]),
        mexer('sonar', giro=0.0125),
        confere('obstaculo a 5 cm: o servo olha para a esquerda (150 graus)', 5, servo={'peca': 'servo', 'min': 140, 'max': 160}),
        confere('e para a direita (30 graus)', 4, servo={'peca': 'servo', 'min': 20, 'max': 40}),
        confere('os dois lados bloqueados: meia-volta', 4, serial='Caminho bloqueado'),
        confere('a re (os quatro para tras)', 3, rpm_sinal=[{'peca': m, 'sinal': -1, 'min': 20} for m in todos]),
        mexer('sonar', giro=0.25),
        confere('caminho livre de novo: volta a andar para a frente', 8, rpm_sinal=[{'peca': m, 'sinal': 1, 'min': 40} for m in todos]),
    ]
    projeto('rover-4wd-hc-sr04', 'Rover 4WD com HC-SR04 (o seu Rover_ponte_H)', [
        'O carrinho classico dos kits: o rover 4WD com o sensor ultrassonico',
        'HC-SR04 num servo SG90, que olha para os lados quando aparece um',
        'obstaculo. O codigo e o seu Rover_ponte_H.ino, sem mudar nada.',
        '',
        'Na barra do sensor, mude a distancia: acima de 10 cm o rover anda; com',
        'o obstaculo perto ele para, olha para a esquerda e para a direita e',
        'vira para o lado mais livre (ou da re e meia-volta).',
        '',
        'Repare: o ENA esta no pino 2, que nao tem PWM no Uno -- o analogWrite',
        'nele so liga ou desliga, e o lado esquerdo anda sempre no maximo (o',
        'comentario do seu codigo ja avisa).',
    ], codigo, pecas, fios, roteiro)


def rover_4wd_reles():
    codigo = codigo_do_usuario(DOWNLOADS + 'Uno_DesvioObstaculos/Uno_DesvioObstaculos.ino')
    if codigo is None:
        print('rover-4wd-reles: Uno_DesvioObstaculos.ino nao esta nos Downloads; pulando')
        return
    pecas, fios = [], []
    y_frente, y_tras = 260, 760
    for m, (x, y, espelhado) in {'m_fe': (180, y_frente, False), 'm_te': (180, y_tras, False),
                                 'm_fd': (760, y_frente, True), 'm_td': (760, y_tras, True)}.items():
        pecas.append(peca(m, 'motor-tt', x, y, Espelhada=espelhado))
        rx, ry = no_eixo('roda-tt', 'motor-tt', x, y, espelhado)
        pecas.append(peca('roda' + m[1:], 'roda-tt', rx, ry))
    pecas += [
        peca('placa', 'arduino-uno', 470, y_frente + 180),
        peca('servo', 'servo', 520, 20),
        peca('sonar', 'hc-sr04', 520, 150, Giro=0.25),
        bateria('baterias', 7.4, 5000, 2600, '2 x 18650 (7,4 V)', 520, y_frente + 640),
    ]
    # Quatro reles (o KS2E: a bobina entre o 5V e o pino -- ativo em LOW, como
    # nos modulos): por lado, A e B. COM do A num fio do motor, COM do B no
    # outro; NO na bateria +, NC na bateria -.
    reles = {'rel_esq_a': ('4', 470, y_frente + 420), 'rel_esq_b': ('5', 570, y_frente + 420),
             'rel_dir_a': ('6', 670, y_frente + 420), 'rel_dir_b': ('7', 770, y_frente + 420)}
    for r, (pino, x, y) in reles.items():
        pecas.append(peca(r, 'ks2e-m-dc5', x, y))
        fios += [fio('placa', '5V', r, 'COIL1', VERMELHO), fio(r, 'COIL2', 'placa', pino, AMARELO),
                 fio('baterias', '+', r, 'NO1', VERMELHO), fio('baterias', '-', r, 'NC1', PRETO)]
    fios += [
        fio('rel_esq_a', 'P1', 'm_fe', '+', VERDE), fio('rel_esq_b', 'P1', 'm_fe', '-', AZUL),
        fio('rel_esq_a', 'P1', 'm_te', '+', VERDE), fio('rel_esq_b', 'P1', 'm_te', '-', AZUL),
        fio('rel_dir_a', 'P1', 'm_fd', '+', VERDE), fio('rel_dir_b', 'P1', 'm_fd', '-', AZUL),
        fio('rel_dir_a', 'P1', 'm_td', '+', VERDE), fio('rel_dir_b', 'P1', 'm_td', '-', AZUL),
        fio('baterias', '+', 'placa', 'VIN', VERMELHO), fio('baterias', '-', 'placa', 'GND.2', PRETO),
        fio('placa', '8', 'sonar', 'TRIG', VERDE), fio('placa', '9', 'sonar', 'ECHO', AZUL),
        fio('placa', '5V', 'sonar', 'VCC', VERMELHO), fio('placa', 'GND.3', 'sonar', 'GND', PRETO),
        fio('placa', '10', 'servo', 'PWM', LARANJA), fio('placa', '5V', 'servo', 'V+', VERMELHO), fio('placa', 'GND.1', 'servo', 'GND', MARROM),
    ]
    todos = ['m_fe', 'm_te', 'm_fd', 'm_td']
    roteiro = [
        confere('o Uno liga pelas baterias no VIN (sem o cabo USB)', 4, energizada=True),
        # A conta do sketch (0,034 cm/us) com o som a 343 m/s: 99 cm.
        confere('com 100 cm livres o sonar le ~100 cm (99, pela conta do sketch)', 6, serial='Frente: 99'),
        confere('e o rover anda para a frente (os reles A ligados)', 3, rpm_sinal=[{'peca': m, 'sinal': 1, 'min': 40} for m in todos]),
        mexer('sonar', giro=0.0125),
        confere('obstaculo a 5 cm: meia-volta', 8, serial='Caminho bloqueado'),
        confere('a re (os reles B ligados)', 3, rpm_sinal=[{'peca': m, 'sinal': -1, 'min': 20} for m in todos]),
        confere('e os reles direto nos pinos nao queimam nada', 1, sem_avisos=True),
    ]
    projeto('rover-4wd-reles', 'Rover 4WD com reles (o seu Uno_DesvioObstaculos)', [
        'O rover 4WD do seu Uno_DesvioObstaculos.ino: em vez da ponte H, quatro',
        'reles -- dois por lado (A = frente, B = re) -- como o comentario do',
        'codigo explica. O codigo e o seu, sem mudar nada.',
        '',
        'O codigo foi escrito para MODULOS de rele (com transistor e LED,',
        'ativos em LOW). O simulador ainda nao tem o modulo: aqui cada rele e o',
        'KS2E solto, com a bobina entre o 5V e o pino (o pino em LOW liga o',
        'rele) -- e a bobina puxa ~70 mA do pino, o dobro do que o Uno aguenta.',
    ], codigo, pecas, fios, roteiro)


# =====================================================================
#  4) OS REGULADORES DE TENSAO
# =====================================================================

def fonte_7805_uno():
    pecas = [
        peca('placa', 'arduino-uno', 40, 40),
        peca('reg', 'regulador-7805', 420, 300),
        bateria('bat', 11.1, 20000, 2200, 'LiPo 3S 2200 mAh (11,1 V)', 560, 330),
        peca('r1', 'resistor', 380, 470, Ohms=10000), peca('r2', 'resistor', 380, 540, Ohms=4700),
    ]
    p_led, f_led = led('led', 'verde', 360, 40, '13', 'GND.1')
    pecas += p_led
    fios = f_led + [
        fio('bat', '+', 'reg', 'IN', VERMELHO), fio('bat', '-', 'reg', 'GND', PRETO),
        fio('reg', 'OUT', 'placa', '5V', VERMELHO), fio('reg', 'GND', 'placa', 'GND.2', PRETO),
        # o divisor: 10k + 4k7 mede a bateria no A0 (11,1 V viram 3,5 V)
        fio('bat', '+', 'r1', '1', VERMELHO), fio('r1', '2', 'placa', 'A0', AMARELO),
        fio('r2', '1', 'placa', 'A0', AMARELO), fio('r2', '2', 'placa', 'GND.3', PRETO),
    ]
    codigo = [
        '// O Uno ligado pelo 7805 (5 V da LiPo 3S), medindo a propria bateria.',
        '// O divisor de 10k + 4k7 leva os 11,1 V da LiPo para ~3,5 V no A0.',
        '',
        'const int LED = 13;',
        'const float DIVISOR = (10000.0 + 4700.0) / 4700.0;',
        '',
        'void setup() {',
        '  Serial.begin(9600);',
        '  pinMode(LED, OUTPUT);',
        '  Serial.println("Uno ligado pelo 7805");',
        '}',
        '',
        'void loop() {',
        '  float volts = analogRead(A0) * 5.0 / 1023.0 * DIVISOR;',
        '  Serial.print("Bateria: ");',
        '  Serial.print(volts, 1);',
        '  Serial.println(" V");',
        '  digitalWrite(LED, !digitalRead(LED));',
        '  delay(500);',
        '}',
    ]
    roteiro = [
        confere('o 7805 liga o Uno (sem o cabo USB)', 4, energizada=True),
        confere('o Uno mede a LiPo pelo divisor (~11 V)', 5, serial='Bateria: 11.'),
        confere('o LED pisca', 3, led={'peca': 'led', 'aceso': True}),
        confere('e o 7805 fica frio (so 0,3 W)', 1, sem_avisos=True),
    ]
    projeto('fonte-7805-uno', 'Uno alimentado pelo 7805 (LiPo 3S)', [
        'O regulador linear 7805 baixa a LiPo 3S (11,1 V) para os 5 V do Uno, que',
        'entram pelo pino 5V -- sem o cabo USB. O Uno mede a propria bateria',
        'por um divisor de resistores no A0 e pisca o LED.',
        '',
        'Clique no 7805: a barra mostra a entrada, a saida, a corrente e a',
        'temperatura. Com so o Uno (~50 mA) ele esquenta pouco: (11,1 - 5) x',
        '0,05 = 0,3 W.',
    ], codigo, pecas, fios, roteiro)


def regulador_7805_servos():
    pecas = [
        peca('placa', 'arduino-uno', 40, 40),
        peca('reg', 'regulador-7805', 420, 330),
        bateria('bat', 12, 20000, 2200, 'Bateria 12 V', 560, 380),
        peca('servo1', 'servo-mg996r', 360, 40), peca('servo2', 'servo-mg996r', 360, 170),
    ]
    fios = [
        fio('bat', '+', 'reg', 'IN', VERMELHO), fio('bat', '-', 'reg', 'GND', PRETO),
        fio('reg', 'OUT', 'placa', '5V', VERMELHO), fio('reg', 'GND', 'placa', 'GND.2', PRETO),
        fio('reg', 'OUT', 'servo1', 'V+', VERMELHO), fio('reg', 'GND', 'servo1', 'GND', MARROM),
        fio('reg', 'OUT', 'servo2', 'V+', VERMELHO), fio('reg', 'GND', 'servo2', 'GND', MARROM),
        fio('placa', '9', 'servo1', 'PWM', LARANJA), fio('placa', '10', 'servo2', 'PWM', LARANJA),
    ]
    codigo = [
        '// Dois servos MG996R e o Uno, todos no 7805 de 12 V -- o erro classico.',
        '// Os servos puxam corrente, o 7805 transforma (12 - 5) V x a corrente em',
        '// calor, e sem dissipador a protecao termica dele desliga a saida.',
        '',
        '#include <Servo.h>',
        '',
        'Servo s1;',
        'Servo s2;',
        '',
        'void setup() {',
        '  Serial.begin(9600);',
        '  s1.attach(9);',
        '  s2.attach(10);',
        '  Serial.println("Uno ligou");',
        '}',
        '',
        'void loop() {',
        '  for (int a = 0; a <= 180; a += 10) { s1.write(a); s2.write(180 - a); delay(40); }',
        '  for (int a = 180; a >= 0; a -= 10) { s1.write(a); s2.write(180 - a); delay(40); }',
        '}',
    ]
    roteiro = [
        confere('o 7805 liga o Uno', 4, energizada=True),
        confere('os servos varrem', 4, servo={'peca': 'servo1', 'min': 120, 'max': 181}),
        confere('o 7805 esquenta e a protecao termica desliga a saida (o aviso explica)', 60, aviso='térmica'),
        confere('e o Uno apaga junto (o classico "o Arduino fica reiniciando")', 5, energizada=False),
    ]
    projeto('7805-esquenta-com-servos', '7805 com dois servos: esquenta e desliga', [
        'O problema classico: dois servos MG996R e o Uno, todos no mesmo 7805,',
        'com 12 V na entrada. Os servos puxam corrente; o 7805 transforma a',
        'diferenca (12 - 5 = 7 V) vezes a corrente em calor, e sem dissipador a',
        'protecao termica dele corta a saida em segundos. O Uno apaga, o 7805',
        'esfria e volta -- e o Arduino "fica reiniciando sozinho".',
        '',
        'Clique no 7805 para ver a temperatura subir na barra. A solucao de',
        'verdade: um regulador chaveado (LM2596) para os servos.',
    ], codigo, pecas, fios, roteiro)


def lm2596_esp32():
    pecas = [
        peca('placa', 'esp32-devkit-v1', 40, 40),
        peca('reg', 'lm2596', 300, 300, Volts=5.0),
        bateria('bat', 11.1, 20000, 2200, 'LiPo 3S 2200 mAh (11,1 V)', 520, 320),
        peca('r1', 'resistor', 200, 120, Ohms=100000), peca('r2', 'resistor', 200, 190, Ohms=22000),
    ]
    p_led, f_led = led('led', 'azul', 260, 0, 'D2', 'GND.1')
    pecas += p_led
    fios = f_led + [
        fio('bat', '+', 'reg', 'IN+', VERMELHO), fio('bat', '-', 'reg', 'IN-', PRETO),
        fio('reg', 'OUT+', 'placa', 'VIN', VERMELHO), fio('reg', 'OUT-', 'placa', 'GND.2', PRETO),
        fio('bat', '+', 'r1', '1', VERMELHO), fio('r1', '2', 'placa', 'D34', AMARELO),
        fio('r2', '1', 'placa', 'D34', AMARELO), fio('r2', '2', 'placa', 'GND.1', PRETO),
    ]
    codigo = [
        '// A ESP32 ligada pelo LM2596 (step-down: 11,1 V -> 5 V no VIN),',
        '// medindo a LiPo por um divisor de 100k + 22k no GPIO34.',
        '',
        'const int LED = 2;',
        'const float DIVISOR = (100000.0 + 22000.0) / 22000.0;',
        '',
        'void setup() {',
        '  Serial.begin(115200);',
        '  pinMode(LED, OUTPUT);',
        '  analogReadResolution(12);',
        '  Serial.println("ESP32 ligada pelo LM2596");',
        '}',
        '',
        'void loop() {',
        '  float volts = analogRead(34) * 3.3 / 4095.0 * DIVISOR;',
        '  Serial.print("Bateria: ");',
        '  Serial.print(volts, 1);',
        '  Serial.println(" V");',
        '  digitalWrite(LED, volts > 10.5);   // aceso: bateria boa',
        '  delay(500);',
        '}',
    ]
    roteiro = [
        confere('o LM2596 liga a ESP32 pelo VIN (sem o cabo USB)', 4, energizada=True),
        confere('a ESP32 mede ~11 V na LiPo', 5, serial='Bateria: 11.'),
        confere('o LED de bateria boa acende', 3, led={'peca': 'led', 'aceso': True}),
        mexer('bat', volts=9.8),
        confere('com a LiPo em 9,8 V o LED de bateria boa apaga', 4, led={'peca': 'led', 'aceso': False}),
        confere('sem aviso nenhum', 1, sem_avisos=True),
    ]
    projeto('lm2596-esp32-medidor', 'ESP32 no LM2596, medindo a LiPo', [
        'O step-down LM2596 (o trimpot em 5 V) baixa a LiPo 3S e liga a ESP32',
        'pelo VIN -- sem o cabo USB. A ESP32 mede a bateria por um divisor de',
        '100k + 22k no GPIO34 (o 11,1 V vira 2 V) e acende o LED enquanto ela',
        'estiver acima de 10,5 V.',
        '',
        'Mude a tensao da bateria na barra dela e veja a leitura e o LED. O',
        'LM2596 e chaveado: da LiPo sai bem menos corrente do que a ESP32',
        'puxa dos 5 V (clique nele).',
    ], codigo, pecas, fios, roteiro)


def mt3608_uno():
    pecas = [
        peca('placa', 'arduino-uno', 40, 40),
        peca('reg', 'mt3608', 340, 300, Volts=5.0),
        bateria('bat', 3.7, 5000, 2600, 'Bateria 18650 (3,7 V)', 540, 320),
        peca('buzzer', 'buzzer', 380, 40),
    ]
    fios = [
        fio('bat', '+', 'reg', 'VIN+', VERMELHO), fio('bat', '-', 'reg', 'VIN-', PRETO),
        fio('reg', 'VOUT+', 'placa', '5V', VERMELHO), fio('reg', 'VOUT-', 'placa', 'GND.2', PRETO),
        fio('placa', '8', 'buzzer', '1', VERDE), fio('buzzer', '2', 'placa', 'GND.1', PRETO),
    ]
    codigo = [
        '// Uma 18650 (3,7 V) e o MT3608 (step-up) ligando o Uno pelo pino 5V.',
        '',
        'const int BUZZER = 8;',
        '',
        'void setup() {',
        '  Serial.begin(9600);',
        '  Serial.println("Uno ligado pelo MT3608");',
        '}',
        '',
        'void loop() {',
        '  tone(BUZZER, 880, 150);',
        '  Serial.println("bip");',
        '  delay(1000);',
        '}',
    ]
    roteiro = [
        confere('o MT3608 sobe os 3,7 V e liga o Uno (sem o cabo USB)', 4, energizada=True),
        confere('o Uno bipa', 4, serial='bip'),
        confere('o buzzer toca', 3, tom=True),
        confere('sem aviso nenhum', 1, sem_avisos=True),
    ]
    projeto('mt3608-18650-uno', 'Uno numa 18650 com o MT3608 (step-up)', [
        'Uma celula 18650 (3,7 V) nao liga o Uno sozinha: o MT3608 (step-up,',
        'o trimpot em 5 V) sobe a tensao e entra pelo pino 5V. O Uno bipa uma',
        'vez por segundo.',
        '',
        'Clique no MT3608: da bateria sai mais corrente do que entra no Uno',
        '(a potencia se conserva: 5 V x I = 3,7 V x I x eficiencia).',
    ], codigo, pecas, fios, roteiro)


def ams1117_lipo_1s():
    pecas = [
        peca('placa', 'esp32-devkit-v1', 40, 40),
        peca('reg', 'ams1117', 300, 320),
        bateria('bat', 4.2, 10000, 1000, 'LiPo 1S 1000 mAh (carregada, 4,2 V)', 460, 320),
    ]
    p_led, f_led = led('led', 'verde', 260, 0, 'D2', 'GND.1')
    pecas += p_led
    fios = f_led + [
        fio('bat', '+', 'reg', 'VIN', VERMELHO), fio('bat', '-', 'reg', 'GND', PRETO),
        fio('reg', 'VOUT', 'placa', '3V3', VERMELHO), fio('reg', 'GND', 'placa', 'GND.1', PRETO),
    ]
    codigo = [
        '// A ESP32 numa LiPo de uma celula pelo AMS1117-3.3.',
        '// O AMS1117 precisa de ~1,1 V de folga: com a LiPo cheia (4,2 V) ele',
        '// da ~3,1 V; com ela em 3,7 V, so 2,6 V -- e a ESP32 nao liga.',
        '',
        'void setup() {',
        '  Serial.begin(115200);',
        '  pinMode(2, OUTPUT);',
        '  Serial.println("ESP32 ligada pelo AMS1117");',
        '}',
        '',
        'void loop() {',
        '  digitalWrite(2, !digitalRead(2));',
        '  delay(400);',
        '}',
    ]
    roteiro = [
        confere('com a LiPo cheia (4,2 V) a ESP32 liga, no limite (~3,1 V no 3V3)', 4, energizada=True),
        confere('e o aviso explica a folga que falta', 3, aviso='precisa de'),
        mexer('bat', volts=3.7),
        confere('com a LiPo em 3,7 V o AMS1117 da ~2,6 V e a ESP32 apaga', 4, energizada=False),
    ]
    projeto('ams1117-lipo-1s-esp32', 'ESP32 numa LiPo 1S pelo AMS1117 (o limite da folga)', [
        'Um erro comum: o AMS1117-3.3 numa LiPo de uma celula. Ele precisa de',
        '~1,1 V a mais na entrada do que na saida: com a LiPo cheia (4,2 V) ele',
        'so consegue ~3,1 V (a ESP32 ainda liga, no limite); com a LiPo em 3,7 V',
        'sai ~2,6 V, e a ESP32 apaga.',
        '',
        'Mude a tensao da bateria na barra dela e veja. Para uma celula de litio',
        'use um regulador de folga pequena (HT7333, ME6211) ou um buck-boost.',
    ], codigo, pecas, fios, roteiro)


# =====================================================================
#  5) OS CLASSICOS
# =====================================================================

def semaforo():
    pecas, fios = [peca('placa', 'arduino-uno', 40, 200)], []
    for id_, cor, pino, x in [('vermelho', 'vermelho', '13', 360), ('amarelo', 'amarelo', '12', 440), ('verde', 'verde', '11', 520)]:
        p, f = led(id_, cor, x, 40, pino, 'GND.1')
        pecas += p
        fios += f
    codigo = [
        '// Semaforo: vermelho 3 s, verde 3 s, amarelo 1 s.',
        'const int VERMELHO = 13, AMARELO = 12, VERDE = 11;',
        '',
        'void acender(int pino, int ms) {',
        '  digitalWrite(VERMELHO, pino == VERMELHO);',
        '  digitalWrite(AMARELO, pino == AMARELO);',
        '  digitalWrite(VERDE, pino == VERDE);',
        '  delay(ms);',
        '}',
        '',
        'void setup() {',
        '  pinMode(VERMELHO, OUTPUT);',
        '  pinMode(AMARELO, OUTPUT);',
        '  pinMode(VERDE, OUTPUT);',
        '}',
        '',
        'void loop() {',
        '  acender(VERMELHO, 3000);',
        '  acender(VERDE, 3000);',
        '  acender(AMARELO, 1000);',
        '}',
    ]
    roteiro = [
        confere('vermelho primeiro', 3, led={'peca': 'vermelho', 'aceso': True}),
        confere('depois o verde', 5, led={'peca': 'verde', 'aceso': True}),
        confere('depois o amarelo', 5, led={'peca': 'amarelo', 'aceso': True}),
    ]
    projeto('semaforo', 'Semaforo (Uno, 3 LEDs)', [
        'O primeiro projeto de todo mundo: tres LEDs com resistor de 220 ohms,',
        'vermelho 3 s, verde 3 s, amarelo 1 s.',
    ], codigo, pecas, fios, roteiro)


def semaforo_pedestre():
    pecas, fios = [peca('placa', 'arduino-uno', 40, 260)], []
    for id_, cor, pino, x in [('c_vermelho', 'vermelho', '12', 340), ('c_amarelo', 'amarelo', '11', 420), ('c_verde', 'verde', '10', 500),
                              ('p_vermelho', 'vermelho', '9', 620), ('p_verde', 'verde', '8', 700)]:
        p, f = led(id_, cor, x, 40, pino, 'GND.1')
        pecas += p
        fios += f
    p, f = botao('botao', 380, 520, '2', 'GND.2')
    pecas += p
    fios += f
    codigo = [
        '// Semaforo de pedestre: o carro fica no verde ate alguem apertar o botao.',
        'const int C_VERMELHO = 12, C_AMARELO = 11, C_VERDE = 10;',
        'const int P_VERMELHO = 9, P_VERDE = 8;',
        'const int BOTAO = 2;',
        '',
        'void carro(int v, int a, int g) { digitalWrite(C_VERMELHO, v); digitalWrite(C_AMARELO, a); digitalWrite(C_VERDE, g); }',
        'void pedestre(int v, int g) { digitalWrite(P_VERMELHO, v); digitalWrite(P_VERDE, g); }',
        '',
        'void setup() {',
        '  Serial.begin(9600);',
        '  for (int p = 8; p <= 12; p++) pinMode(p, OUTPUT);',
        '  pinMode(BOTAO, INPUT_PULLUP);',
        '  carro(LOW, LOW, HIGH);',
        '  pedestre(HIGH, LOW);',
        '}',
        '',
        'void loop() {',
        '  if (digitalRead(BOTAO) == LOW) {',
        '    Serial.println("pedestre pediu para atravessar");',
        '    carro(LOW, HIGH, LOW);',
        '    delay(2000);',
        '    carro(HIGH, LOW, LOW);',
        '    pedestre(LOW, HIGH);',
        '    delay(4000);',
        '    pedestre(HIGH, LOW);',
        '    carro(LOW, LOW, HIGH);',
        '  }',
        '}',
    ]
    roteiro = [
        confere('o carro fica no verde', 3, led={'peca': 'c_verde', 'aceso': True}),
        clicar('botao'),
        confere('o botao chama o pedestre', 2, serial='pedestre pediu'),
        confere('amarelo para o carro', 2, led={'peca': 'c_amarelo', 'aceso': True}),
        confere('e verde para o pedestre', 4, led={'peca': 'p_verde', 'aceso': True}),
        confere('depois o carro volta ao verde', 6, led={'peca': 'c_verde', 'aceso': True}),
    ]
    projeto('semaforo-pedestre', 'Semaforo com botao de pedestre (Uno)', [
        'Cinco LEDs (tres do carro, dois do pedestre) e um botao com INPUT_PULLUP:',
        'aperte o botao e o semaforo fecha para o carro e abre para o pedestre.',
    ], codigo, pecas, fios, roteiro)


def botao_esp32():
    pecas = [peca('placa', 'esp32-devkit-v1', 40, 40)]
    p, f = led('led', 'vermelho', 280, 20, 'D2', 'GND.1')
    pb, fb = botao('botao', 260, 230, 'D4', 'GND.1')
    codigo = [
        '// Liga e desliga o LED a cada toque no botao (com o "debounce").',
        'const int LED = 2;',
        'const int BOTAO = 4;',
        'bool aceso = false;',
        'int antes = HIGH;',
        '',
        'void setup() {',
        '  Serial.begin(115200);',
        '  pinMode(LED, OUTPUT);',
        '  pinMode(BOTAO, INPUT_PULLUP);',
        '}',
        '',
        'void loop() {',
        '  int agora = digitalRead(BOTAO);',
        '  if (antes == HIGH && agora == LOW) {',
        '    aceso = !aceso;',
        '    digitalWrite(LED, aceso);',
        '    Serial.println(aceso ? "LED aceso" : "LED apagado");',
        '    delay(50);',
        '  }',
        '  antes = agora;',
        '}',
    ]
    roteiro = [
        confere('comeca apagado', 2, led={'peca': 'led', 'aceso': False}),
        clicar('botao'),
        confere('um toque acende', 2, led={'peca': 'led', 'aceso': True}),
        clicar('botao'),
        confere('outro toque apaga', 2, led={'peca': 'led', 'aceso': False}),
    ]
    projeto('botao-liga-desliga-esp32', 'Botao liga/desliga (ESP32)', [
        'O botao com INPUT_PULLUP alterna o LED a cada toque, detectando a borda',
        '(o momento em que ele vai de solto para apertado).',
    ], codigo, pecas + p + pb, f + fb, roteiro)


def dimmer():
    pecas = [peca('placa', 'arduino-uno', 40, 200), peca('pot', 'potentiometer', 380, 330, Giro=0.1)]
    p, f = led('led', 'amarelo', 380, 40, '9', 'GND.1')
    fios = f + [fio('pot', 'VCC', 'placa', '5V', VERMELHO), fio('pot', 'GND', 'placa', 'GND.2', PRETO), fio('pot', 'SIG', 'placa', 'A0', AMARELO)]
    codigo = [
        '// O potenciometro regula o brilho do LED (PWM no pino 9).',
        'void setup() {',
        '  pinMode(9, OUTPUT);',
        '}',
        '',
        'void loop() {',
        '  int leitura = analogRead(A0);          // 0 a 1023',
        '  analogWrite(9, leitura / 4);           // 0 a 255',
        '  delay(10);',
        '}',
    ]
    roteiro = [
        confere('o potenciometro baixo deixa o LED fraco', 3, brilho={'peca': 'led', 'max': 0.35}),
        mexer('pot', giro=0.95),
        confere('no alto, o LED forte', 3, brilho={'peca': 'led', 'min': 0.7}),
    ]
    projeto('dimmer-potenciometro', 'Dimmer com potenciometro (Uno, PWM)', [
        'O analogRead do potenciometro (0 a 1023) vira o PWM do LED (0 a 255).',
        'Gire o potenciometro na barra dele.',
    ], codigo, pecas + p, fios, roteiro)


def servo_pot():
    pecas = [peca('placa', 'arduino-uno', 40, 200), peca('pot', 'potentiometer', 380, 330, Giro=0.0), peca('servo', 'servo', 380, 40)]
    fios = [
        fio('pot', 'VCC', 'placa', '5V', VERMELHO), fio('pot', 'GND', 'placa', 'GND.2', PRETO), fio('pot', 'SIG', 'placa', 'A0', AMARELO),
        fio('placa', '9', 'servo', 'PWM', LARANJA), fio('placa', '5V', 'servo', 'V+', VERMELHO), fio('placa', 'GND.1', 'servo', 'GND', MARROM),
    ]
    codigo = [
        '// O servo segue o potenciometro.',
        '#include <Servo.h>',
        'Servo servo;',
        '',
        'void setup() {',
        '  servo.attach(9);',
        '}',
        '',
        'void loop() {',
        '  servo.write(map(analogRead(A0), 0, 1023, 0, 180));',
        '  delay(15);',
        '}',
    ]
    roteiro = [
        confere('potenciometro no zero: servo em ~0 grau', 3, servo={'peca': 'servo', 'min': -1, 'max': 8}),
        mexer('pot', giro=1.0),
        confere('potenciometro no maximo: servo em ~180 graus', 3, servo={'peca': 'servo', 'min': 172, 'max': 181}),
    ]
    projeto('servo-potenciometro', 'Servo controlado por potenciometro (Uno)', [
        'O map() leva a leitura do potenciometro (0 a 1023) para o angulo do',
        'servo (0 a 180 graus). Gire o potenciometro na barra dele.',
    ], codigo, pecas, fios, roteiro)


def sensor_de_re():
    pecas = [peca('placa', 'arduino-uno', 40, 200), peca('sonar', 'hc-sr04', 380, 40, Giro=0.05), peca('buzzer', 'buzzer', 600, 300)]
    fios = [
        fio('placa', '9', 'sonar', 'TRIG', VERDE), fio('placa', '10', 'sonar', 'ECHO', AZUL),
        fio('placa', '5V', 'sonar', 'VCC', VERMELHO), fio('placa', 'GND.2', 'sonar', 'GND', PRETO),
        fio('placa', '8', 'buzzer', '1', LARANJA), fio('buzzer', '2', 'placa', 'GND.1', PRETO),
    ]
    codigo = [
        '// Sensor de re: o bipe acelera conforme o obstaculo chega perto.',
        'const int TRIG = 9, ECHO = 10, BUZZER = 8;',
        '',
        'long distancia() {',
        '  digitalWrite(TRIG, LOW);',
        '  delayMicroseconds(2);',
        '  digitalWrite(TRIG, HIGH);',
        '  delayMicroseconds(10);',
        '  digitalWrite(TRIG, LOW);',
        '  long tempo = pulseIn(ECHO, HIGH, 30000);',
        '  return tempo == 0 ? 400 : tempo / 58;',
        '}',
        '',
        'void setup() {',
        '  Serial.begin(9600);',
        '  pinMode(TRIG, OUTPUT);',
        '  pinMode(ECHO, INPUT);',
        '}',
        '',
        'void loop() {',
        '  long cm = distancia();',
        '  Serial.print(cm);',
        '  Serial.println(" cm");',
        '  if (cm < 10) {',
        '    tone(BUZZER, 1500);          // muito perto: som continuo',
        '    delay(100);',
        '  } else if (cm < 100) {',
        '    tone(BUZZER, 1500, 60);',
        '    delay(cm * 5);               // mais perto, mais rapido',
        '  } else {',
        '    noTone(BUZZER);',
        '    delay(200);',
        '  }',
        '}',
    ]
    roteiro = [
        confere('o sensor le 20 cm', 4, serial='20 cm'),
        confere('e o buzzer bipa', 3, tom=True),
        mexer('sonar', giro=0.5),
        confere('a 200 cm ele le 200 cm', 4, serial='200 cm'),
        confere('e fica quieto', 3, tom=False),
    ]
    projeto('sensor-de-re', 'Sensor de re (HC-SR04 e buzzer)', [
        'O sensor de estacionamento: o HC-SR04 mede a distancia e o buzzer bipa',
        'mais rapido conforme o obstaculo chega perto (abaixo de 10 cm, som',
        'continuo). Mude a distancia na barra do sensor.',
    ], codigo, pecas, fios, roteiro)


def termometro_lcd():
    pecas = [peca('placa', 'arduino-uno', 40, 220), peca('dht', 'dht22', 380, 60, Giro=0.5417), peca('lcd', 'lcd1602-i2c', 520, 220)]
    fios = [
        fio('dht', 'VCC', 'placa', '5V', VERMELHO), fio('dht', 'GND', 'placa', 'GND.2', PRETO), fio('dht', 'SDA', 'placa', '2', AMARELO),
        fio('lcd', 'VCC', 'placa', '5V', VERMELHO), fio('lcd', 'GND', 'placa', 'GND.3', PRETO),
        fio('lcd', 'SDA', 'placa', 'A4', AZUL), fio('lcd', 'SCL', 'placa', 'A5', VERDE),
    ]
    codigo = [
        '// Termometro: o DHT22 no pino 2 e o LCD 16x2 I2C (endereco 0x27).',
        '#include <DHT.h>',
        '#include <Wire.h>',
        '#include <LiquidCrystal_I2C.h>',
        '',
        'DHT dht(2, DHT22);',
        'LiquidCrystal_I2C lcd(0x27, 16, 2);',
        '',
        'void setup() {',
        '  dht.begin();',
        '  lcd.init();',
        '  lcd.backlight();',
        '  lcd.print("Termometro");',
        '}',
        '',
        'void loop() {',
        '  delay(2000);',
        '  float t = dht.readTemperature();',
        '  float u = dht.readHumidity();',
        '  lcd.clear();',
        '  lcd.setCursor(0, 0);',
        '  lcd.print("Temp: ");',
        '  lcd.print(t, 1);',
        '  lcd.print(" C");',
        '  lcd.setCursor(0, 1);',
        '  lcd.print("Umid: ");',
        '  lcd.print(u, 0);',
        '  lcd.print(" %");',
        '}',
    ]
    roteiro = [
        confere('o LCD mostra a temperatura do DHT22 (25 C)', 6, lcd='Temp: 25.0'),
        mexer('dht', giro=0.75),
        confere('a 50 C o LCD acompanha', 6, lcd='Temp: 50.0'),
    ]
    projeto('termometro-dht22-lcd', 'Termometro com DHT22 e LCD 16x2 I2C (Uno)', [
        'O DHT22 mede temperatura e umidade; o LCD 16x2 com modulo I2C mostra as',
        'duas a cada 2 segundos. Mude a temperatura na barra do sensor.',
    ], codigo, pecas, fios, roteiro)


def alarme_pir():
    pecas = [peca('placa', 'arduino-uno', 40, 220), peca('pir', 'pir-motion-sensor', 380, 60), peca('buzzer', 'buzzer', 560, 60)]
    p, f = led('led', 'vermelho', 700, 60, '13', 'GND.1')
    fios = f + [
        fio('pir', 'VCC', 'placa', '5V', VERMELHO), fio('pir', 'GND', 'placa', 'GND.2', PRETO), fio('pir', 'OUT', 'placa', '2', AMARELO),
        fio('placa', '8', 'buzzer', '1', LARANJA), fio('buzzer', '2', 'placa', 'GND.3', PRETO),
    ]
    codigo = [
        '// Alarme: o sensor PIR ve movimento, a sirene toca e o LED pisca.',
        'const int PIR = 2, BUZZER = 8, LED = 13;',
        '',
        'void setup() {',
        '  Serial.begin(9600);',
        '  pinMode(PIR, INPUT);',
        '  pinMode(LED, OUTPUT);',
        '  Serial.println("alarme armado");',
        '}',
        '',
        'void loop() {',
        '  if (digitalRead(PIR) == HIGH) {',
        '    Serial.println("MOVIMENTO!");',
        '    for (int i = 0; i < 6; i++) {',
        '      digitalWrite(LED, HIGH);',
        '      tone(BUZZER, 2000);',
        '      delay(150);',
        '      digitalWrite(LED, LOW);',
        '      tone(BUZZER, 1200);',
        '      delay(150);',
        '    }',
        '    noTone(BUZZER);',
        '  }',
        '}',
    ]
    roteiro = [
        confere('o alarme arma', 3, serial='alarme armado'),
        confere('quieto sem movimento', 2, tom=False),
        mexer('pir', giro=1.0),
        confere('o PIR ve movimento', 3, serial='MOVIMENTO'),
        confere('e a sirene toca', 2, tom=True),
        mexer('pir', giro=0.0),
    ]
    projeto('alarme-pir', 'Alarme com sensor de presenca PIR (Uno)', [
        'O sensor PIR poe HIGH na saida quando ve movimento: a sirene (buzzer)',
        'toca e o LED pisca. Na barra do PIR, ligue o "movimento".',
    ], codigo, pecas + p, fios, roteiro)


def luz_noturna():
    pecas = [peca('placa', 'esp32-devkit-v1', 40, 40), peca('ldr', 'photoresistor-sensor', 260, 300, Giro=0.9)]
    p, f = led('led', 'branco', 300, 20, 'D2', 'GND.1')
    fios = f + [fio('ldr', 'VCC', 'placa', '3V3', VERMELHO), fio('ldr', 'GND', 'placa', 'GND.1', PRETO), fio('ldr', 'AO', 'placa', 'D34', AMARELO)]
    codigo = [
        '// Luz noturna: o LDR mede a luz, e no escuro o LED acende.',
        'const int LDR = 34;',
        'const int LED = 2;',
        '',
        'void setup() {',
        '  Serial.begin(115200);',
        '  pinMode(LED, OUTPUT);',
        '}',
        '',
        'void loop() {',
        '  int luz = analogRead(LDR);   // no modulo, mais luz = tensao MENOR',
        '  bool escuro = luz > 2500;',
        '  digitalWrite(LED, escuro);',
        '  Serial.print("leitura do LDR: ");',
        '  Serial.print(luz);',
        '  Serial.println(escuro ? " (escuro: LED aceso)" : " (claro)");',
        '  delay(300);',
        '}',
    ]
    roteiro = [
        confere('no claro o LED fica apagado', 3, led={'peca': 'led', 'aceso': False}),
        mexer('ldr', giro=0.05),
        confere('no escuro o LED acende', 3, led={'peca': 'led', 'aceso': True}),
    ]
    projeto('luz-noturna-ldr', 'Luz noturna com LDR (ESP32)', [
        'O modulo de LDR (fotoresistor) no GPIO34: no escuro a leitura sobe e a',
        'ESP32 acende o LED. Mude a luz na barra do sensor.',
    ], codigo, pecas + p, fios, roteiro)


def contador_7seg():
    pecas = [peca('placa', 'arduino-uno', 40, 220), peca('display', '7segment', 420, 40)]
    pb, fb = botao('botao', 560, 60, 'A0', 'GND.3')
    segmentos = ['A', 'B', 'C', 'D', 'E', 'F', 'G']
    fios = [fio('placa', str(2 + i), 'display', s, VERDE) for i, s in enumerate(segmentos)]
    fios += [fio('display', 'COM.1', 'placa', 'GND.1', PRETO)]
    codigo = [
        '// Contador de 0 a 9 no display de 7 segmentos (catodo comum).',
        '// Os segmentos a..g nos pinos 2..8; o botao no A0.',
        'const byte DIGITOS[10] = {',
        '  0b0111111, 0b0000110, 0b1011011, 0b1001111, 0b1100110,',
        '  0b1101101, 0b1111101, 0b0000111, 0b1111111, 0b1101111',
        '};',
        'int contagem = 0;',
        'int antes = HIGH;',
        '',
        'void mostrar(int n) {',
        '  for (int s = 0; s < 7; s++) digitalWrite(2 + s, (DIGITOS[n] >> s) & 1);',
        '}',
        '',
        'void setup() {',
        '  Serial.begin(9600);',
        '  for (int p = 2; p <= 8; p++) pinMode(p, OUTPUT);',
        '  pinMode(A0, INPUT_PULLUP);',
        '  mostrar(0);',
        '}',
        '',
        'void loop() {',
        '  int agora = digitalRead(A0);',
        '  if (antes == HIGH && agora == LOW) {',
        '    contagem = (contagem + 1) % 10;',
        '    mostrar(contagem);',
        '    Serial.println(contagem);',
        '    delay(50);',
        '  }',
        '  antes = agora;',
        '}',
    ]
    roteiro = clicar('botao') + clicar('botao') + clicar('botao') + [
        confere('tres toques: o display mostra 3 (a, b, c, d, g acesos; e, f apagados)', 2,
                pinos={'2': 1, '3': 1, '4': 1, '5': 1, '6': 0, '7': 0, '8': 1}),
        confere('e o monitor conta', 1, serial='3'),
    ]
    projeto('contador-7-segmentos', 'Contador no display de 7 segmentos (Uno)', [
        'Um display de 7 segmentos de catodo comum (os segmentos nos pinos 2 a',
        '8) conta de 0 a 9 a cada toque no botao.',
    ], codigo, pecas + pb, fios + fb, roteiro)


def relogio_rtc():
    pecas = [peca('placa', 'arduino-uno', 40, 220), peca('rtc', 'ds1307', 380, 40), peca('lcd', 'lcd1602-i2c', 520, 220)]
    fios = [
        fio('rtc', '5V', 'placa', '5V', VERMELHO), fio('rtc', 'GND', 'placa', 'GND.2', PRETO),
        fio('rtc', 'SDA', 'placa', 'A4', AZUL), fio('rtc', 'SCL', 'placa', 'A5', VERDE),
        fio('lcd', 'VCC', 'placa', '5V', VERMELHO), fio('lcd', 'GND', 'placa', 'GND.3', PRETO),
        fio('lcd', 'SDA', 'placa', 'A4', AZUL), fio('lcd', 'SCL', 'placa', 'A5', VERDE),
    ]
    codigo = [
        '// Relogio: o RTC DS1307 guarda a hora; o LCD I2C mostra.',
        '#include <Wire.h>',
        '#include <RTClib.h>',
        '#include <LiquidCrystal_I2C.h>',
        '',
        'RTC_DS1307 rtc;',
        'LiquidCrystal_I2C lcd(0x27, 16, 2);',
        '',
        'void doisDigitos(int n) {',
        '  if (n < 10) lcd.print(0);',
        '  lcd.print(n);',
        '}',
        '',
        'void setup() {',
        '  lcd.init();',
        '  lcd.backlight();',
        '  rtc.begin();',
        '  if (!rtc.isrunning()) rtc.adjust(DateTime(F(__DATE__), F(__TIME__)));',
        '}',
        '',
        'void loop() {',
        '  DateTime agora = rtc.now();',
        '  lcd.setCursor(0, 0);',
        '  lcd.print("Hora ");',
        '  doisDigitos(agora.hour());',
        '  lcd.print(":");',
        '  doisDigitos(agora.minute());',
        '  lcd.print(":");',
        '  doisDigitos(agora.second());',
        '  lcd.setCursor(0, 1);',
        '  doisDigitos(agora.day());',
        '  lcd.print("/");',
        '  doisDigitos(agora.month());',
        '  lcd.print("/");',
        '  lcd.print(agora.year());',
        '  delay(500);',
        '}',
    ]
    roteiro = [
        confere('o LCD mostra a hora do RTC', 5, lcd='Hora '),
        confere('e a data', 2, lcd='/20'),
    ]
    projeto('relogio-ds1307-lcd', 'Relogio com RTC DS1307 e LCD I2C (Uno)', [
        'O DS1307 guarda a hora (com a bateria dele); o LCD 16x2 I2C mostra a',
        'hora e a data. Os dois dividem o barramento I2C (A4 e A5).',
    ], codigo, pecas, fios, roteiro)


def fechadura():
    pecas = [peca('placa', 'arduino-uno', 40, 360), peca('teclado', 'membrane-keypad', 380, 20), peca('trava', 'servo', 700, 400)]
    p, f = led('led', 'verde', 720, 220, '13', 'GND.1')
    linhas, colunas = ['9', '8', '7', '6'], ['5', '4', '3', '2']
    fios = [fio('teclado', f'R{i + 1}', 'placa', p_, AZUL) for i, p_ in enumerate(linhas)]
    fios += [fio('teclado', f'C{i + 1}', 'placa', p_, VERDE) for i, p_ in enumerate(colunas)]
    fios += f + [fio('placa', '10', 'trava', 'PWM', LARANJA), fio('placa', '5V', 'trava', 'V+', VERMELHO), fio('placa', 'GND.2', 'trava', 'GND', MARROM)]
    codigo = [
        '// Fechadura com senha: teclado 4x4, servo de trava e LED.',
        '#include <Keypad.h>',
        '#include <Servo.h>',
        '',
        'const byte LINHAS = 4, COLUNAS = 4;',
        'char teclas[LINHAS][COLUNAS] = {',
        "  {'1', '2', '3', 'A'},",
        "  {'4', '5', '6', 'B'},",
        "  {'7', '8', '9', 'C'},",
        "  {'*', '0', '#', 'D'},",
        '};',
        'byte pinosLinhas[LINHAS] = {9, 8, 7, 6};',
        'byte pinosColunas[COLUNAS] = {5, 4, 3, 2};',
        'Keypad teclado = Keypad(makeKeymap(teclas), pinosLinhas, pinosColunas, LINHAS, COLUNAS);',
        'Servo trava;',
        'const String SENHA = "1234";',
        'String digitado = "";',
        '',
        'void setup() {',
        '  Serial.begin(9600);',
        '  pinMode(13, OUTPUT);',
        '  trava.attach(10);',
        '  trava.write(0);   // trancada',
        '  Serial.println("Digite a senha e aperte #");',
        '}',
        '',
        'void loop() {',
        '  char tecla = teclado.getKey();',
        '  if (!tecla) return;',
        "  if (tecla == '#') {",
        '    if (digitado == SENHA) {',
        '      Serial.println("Liberado!");',
        '      digitalWrite(13, HIGH);',
        '      trava.write(90);',
        '      delay(3000);',
        '      trava.write(0);',
        '      digitalWrite(13, LOW);',
        '    } else {',
        '      Serial.println("Senha errada");',
        '    }',
        '    digitado = "";',
        "  } else if (tecla == '*') {",
        '    digitado = "";',
        '  } else {',
        '    digitado += tecla;',
        '  }',
        '}',
    ]
    roteiro = []
    for t in '1234#':
        roteiro += [tecla('teclado', t), esperar(0.25), tecla('teclado', ''), esperar(0.2)]
    roteiro += [
        confere('a senha certa libera', 3, serial='Liberado'),
        confere('o servo destranca (90 graus)', 3, servo={'peca': 'trava', 'min': 80, 'max': 100}),
        confere('e o LED verde acende', 2, led={'peca': 'led', 'aceso': True}),
    ]
    projeto('fechadura-teclado-servo', 'Fechadura com senha (teclado 4x4 e servo)', [
        'O teclado matricial 4x4 (biblioteca Keypad) le a senha; com "1234" e #',
        'o servo destranca por 3 segundos e o LED verde acende. * apaga o que',
        'foi digitado. Clique nas teclas do teclado.',
    ], codigo, pecas + p, fios, roteiro)


def controle_ir():
    pecas = [peca('placa', 'arduino-uno', 40, 220), peca('ir', 'ir-receiver', 380, 60), peca('controle', 'ir-remote', 760, 20)]
    fios = [fio('ir', 'VCC', 'placa', '5V', VERMELHO), fio('ir', 'GND', 'placa', 'GND.2', PRETO), fio('ir', 'DAT', 'placa', '11', AMARELO)]
    for id_, cor, pino, x in [('led1', 'vermelho', '2', 460), ('led2', 'verde', '3', 540), ('led3', 'azul', '4', 620)]:
        p, f = led(id_, cor, x, 60, pino, 'GND.1')
        pecas += p
        fios += f
    codigo = [
        '// Controle remoto infravermelho: as teclas 1, 2 e 3 ligam e desligam',
        '// os tres LEDs (biblioteca IRremote).',
        '#include <IRremote.h>',
        '',
        'IRrecv receptor(11);',
        'decode_results resultado;',
        '',
        'void alternar(int pino) {',
        '  digitalWrite(pino, !digitalRead(pino));',
        '}',
        '',
        'void setup() {',
        '  Serial.begin(9600);',
        '  pinMode(2, OUTPUT);',
        '  pinMode(3, OUTPUT);',
        '  pinMode(4, OUTPUT);',
        '  receptor.enableIRIn();',
        '}',
        '',
        'void loop() {',
        '  if (receptor.decode(&resultado)) {',
        '    Serial.print("codigo: 0x");',
        '    Serial.println(resultado.value, HEX);',
        '    if (resultado.value == 0xFF30CF) alternar(2);   // tecla 1',
        '    if (resultado.value == 0xFF18E7) alternar(3);   // tecla 2',
        '    if (resultado.value == 0xFF7A85) alternar(4);   // tecla 3',
        '    receptor.resume();',
        '  }',
        '}',
    ]
    roteiro = [
        esperar(1.5),   # o Uno acabou de ligar: a tecla antes do setup() se perde, como na bancada
        {'controle_ir': 'controle', 'botao': '1'},
        confere('a tecla 1 chega (0xFF30CF)', 3, serial='FF30CF'),
        confere('e acende o LED 1', 2, led={'peca': 'led1', 'aceso': True}),
        {'controle_ir': 'controle', 'botao': '3'},
        confere('a tecla 3 acende o LED 3', 3, led={'peca': 'led3', 'aceso': True}),
    ]
    projeto('controle-ir-leds', 'Controle remoto infravermelho (IRremote, Uno)', [
        'O receptor infravermelho no pino 11 e o controle de 21 teclas dos kits:',
        'as teclas 1, 2 e 3 ligam e desligam os tres LEDs. O monitor mostra o',
        'codigo de cada tecla (o mesmo dos tutoriais: 0xFF30CF e o 1).',
    ], codigo, pecas, fios, roteiro)


def joystick_servos():
    pecas = [peca('placa', 'arduino-uno', 40, 220), peca('joy', 'analog-joystick', 380, 260, Giro=0.5, Giro2=0.5),
             peca('pan', 'servo', 380, 20), peca('tilt', 'servo', 600, 20)]
    fios = [
        fio('joy', 'VCC', 'placa', '5V', VERMELHO), fio('joy', 'GND', 'placa', 'GND.2', PRETO),
        fio('joy', 'HORZ', 'placa', 'A0', AMARELO), fio('joy', 'VERT', 'placa', 'A1', VERDE),
        fio('placa', '9', 'pan', 'PWM', LARANJA), fio('placa', '5V', 'pan', 'V+', VERMELHO), fio('placa', 'GND.1', 'pan', 'GND', MARROM),
        fio('placa', '10', 'tilt', 'PWM', LARANJA), fio('placa', '5V', 'tilt', 'V+', VERMELHO), fio('placa', 'GND.3', 'tilt', 'GND', MARROM),
    ]
    codigo = [
        '// Pan/tilt: o joystick move dois servos.',
        '#include <Servo.h>',
        'Servo pan, tilt;',
        '',
        'void setup() {',
        '  pan.attach(9);',
        '  tilt.attach(10);',
        '}',
        '',
        'void loop() {',
        '  pan.write(map(analogRead(A0), 0, 1023, 0, 180));',
        '  tilt.write(map(analogRead(A1), 0, 1023, 0, 180));',
        '  delay(15);',
        '}',
    ]
    roteiro = [
        confere('com o joystick no centro, os dois servos em ~90 graus', 3, servo={'peca': 'pan', 'min': 80, 'max': 100}),
        mexer('joy', giro=1.0),
        confere('joystick para um lado: o pan vai ao extremo', 3, servo={'peca': 'pan', 'min': 170, 'max': 181}),
        mexer('joy', giro2=0.0),
        confere('para baixo: o tilt vai ao outro extremo', 3, servo={'peca': 'tilt', 'min': -1, 'max': 10}),
    ]
    projeto('joystick-pan-tilt', 'Pan/tilt com joystick e dois servos (Uno)', [
        'Os dois eixos do joystick analogico (A0 e A1) movem dois servos -- o',
        'suporte pan/tilt de camera. Mexa o joystick na barra dele.',
    ], codigo, pecas, fios, roteiro)


def anel_neopixel():
    pecas = [peca('placa', 'esp32-devkit-v1', 40, 40), peca('anel', 'led-ring', 300, 60)]
    fios = [fio('anel', 'VCC', 'placa', 'VIN', VERMELHO), fio('anel', 'GND', 'placa', 'GND.1', PRETO), fio('anel', 'DIN', 'placa', 'D5', VERDE)]
    codigo = [
        '// Arco-iris girando num anel de 16 LEDs WS2812 (NeoPixel).',
        '#include <Adafruit_NeoPixel.h>',
        'Adafruit_NeoPixel anel(16, 5, NEO_GRB + NEO_KHZ800);',
        'long matiz = 0;',
        '',
        'void setup() {',
        '  anel.begin();',
        '  anel.setBrightness(60);',
        '}',
        '',
        'void loop() {',
        '  for (int i = 0; i < 16; i++) anel.setPixelColor(i, anel.ColorHSV(matiz + i * 4096));',
        '  anel.show();',
        '  matiz += 256;',
        '  delay(20);',
        '}',
    ]
    roteiro = [confere('o anel acende (arco-iris)', 4, fita_acesa={'gpio': 5, 'min': 12})]
    projeto('anel-neopixel-esp32', 'Anel NeoPixel arco-iris (ESP32)', [
        'Dezesseis LEDs WS2812 (NeoPixel) num anel, ligados no GPIO5: o',
        'arco-iris gira com o ColorHSV da biblioteca da Adafruit.',
    ], codigo, pecas, fios, roteiro)


def oled_grafico():
    pecas = [peca('placa', 'esp32-devkit-v1', 40, 40), peca('oled', 'ssd1306-i2c', 300, 40), peca('pot', 'potentiometer', 300, 230, Giro=0.6)]
    fios = [
        fio('oled', 'VCC', 'placa', '3V3', VERMELHO), fio('oled', 'GND', 'placa', 'GND.1', PRETO),
        fio('oled', 'SDA', 'placa', 'D21', AZUL), fio('oled', 'SCL', 'placa', 'D22', VERDE),
        fio('pot', 'VCC', 'placa', '3V3', VERMELHO), fio('pot', 'GND', 'placa', 'GND.2', PRETO), fio('pot', 'SIG', 'placa', 'D34', AMARELO),
    ]
    codigo = [
        '// O potenciometro numa barra no OLED 128x64 (SSD1306 I2C).',
        '#include <Wire.h>',
        '#include <Adafruit_GFX.h>',
        '#include <Adafruit_SSD1306.h>',
        '',
        'Adafruit_SSD1306 tela(128, 64, &Wire, -1);',
        '',
        'void setup() {',
        '  Serial.begin(115200);',
        '  tela.begin(SSD1306_SWITCHCAPVCC, 0x3C);',
        '  tela.setTextColor(SSD1306_WHITE);',
        '}',
        '',
        'void loop() {',
        '  int leitura = analogRead(34);',
        '  int largura = map(leitura, 0, 4095, 0, 124);',
        '  tela.clearDisplay();',
        '  tela.setTextSize(1);',
        '  tela.setCursor(0, 0);',
        '  tela.print("Potenciometro: ");',
        '  tela.print(leitura);',
        '  tela.drawRect(0, 20, 128, 20, SSD1306_WHITE);',
        '  tela.fillRect(2, 22, largura, 16, SSD1306_WHITE);',
        '  tela.display();',
        '  delay(50);',
        '}',
    ]
    roteiro = [confere('o OLED desenha o texto e a barra', 4, tela_min={'peca': 'oled', 'valor': 300})]
    projeto('oled-barra-potenciometro', 'Barra no OLED SSD1306 (ESP32)', [
        'O OLED de 128x64 (I2C, 0x3C) mostra a leitura do potenciometro e uma',
        'barra que cresce com ela. Gire o potenciometro.',
    ], codigo, pecas, fios, roteiro)


def inclinometro():
    pecas = [peca('placa', 'esp32-devkit-v1', 40, 40), peca('mpu', 'mpu6050', 300, 60, Giro=0.5)]
    fios = [fio('mpu', 'VCC', 'placa', '3V3', VERMELHO), fio('mpu', 'GND', 'placa', 'GND.1', PRETO),
            fio('mpu', 'SDA', 'placa', 'D21', AZUL), fio('mpu', 'SCL', 'placa', 'D22', VERDE)]
    p, f = led('led', 'vermelho', 300, 220, 'D2', 'GND.1')
    codigo = [
        '// Nivel eletronico: o acelerometro do MPU6050 diz a inclinacao; o LED',
        '// acende quando passa de 20 graus.',
        '#include <Wire.h>',
        '#include <Adafruit_MPU6050.h>',
        '',
        'Adafruit_MPU6050 mpu;',
        '',
        'void setup() {',
        '  Serial.begin(115200);',
        '  pinMode(2, OUTPUT);',
        '  if (!mpu.begin()) Serial.println("MPU6050 nao encontrado");',
        '}',
        '',
        'void loop() {',
        '  sensors_event_t a, g, temp;',
        '  mpu.getEvent(&a, &g, &temp);',
        '  float graus = atan2(a.acceleration.x, a.acceleration.z) * 57.3;',
        '  Serial.print("inclinacao: ");',
        '  Serial.print(graus, 0);',
        '  Serial.println(" graus");',
        '  digitalWrite(2, abs(graus) > 20);',
        '  delay(200);',
        '}',
    ]
    roteiro = [
        confere('na horizontal o LED fica apagado', 3, led={'peca': 'led', 'aceso': False}),
        mexer('mpu', giro=0.85),
        confere('inclinado o LED acende', 3, led={'peca': 'led', 'aceso': True}),
        confere('e o monitor mostra a inclinacao', 1, serial='inclinacao'),
    ]
    projeto('nivel-mpu6050', 'Nivel eletronico com MPU6050 (ESP32)', [
        'O acelerometro do MPU6050 mede a gravidade nos eixos: o atan2 da a',
        'inclinacao, e o LED acende acima de 20 graus. Mude a inclinacao na',
        'barra do sensor.',
    ], codigo, pecas + p, fios + f, roteiro)


def passo_28byj():
    pecas = [peca('placa', 'arduino-uno', 40, 220), peca('driver', 'uln2003', 380, 60), peca('motor', 'motor-28byj48', 600, 20)]
    fios = [
        fio('placa', '8', 'driver', 'IN1', AZUL), fio('placa', '9', 'driver', 'IN2', ROXO),
        fio('placa', '10', 'driver', 'IN3', AMARELO), fio('placa', '11', 'driver', 'IN4', LARANJA),
        fio('placa', '5V', 'driver', '+', VERMELHO), fio('placa', 'GND.2', 'driver', '-', PRETO),
        fio('driver', 'A', 'motor', 'azul', AZUL), fio('driver', 'B', 'motor', 'rosa', ROXO),
        fio('driver', 'C', 'motor', 'amarelo', AMARELO), fio('driver', 'D', 'motor', 'laranja', LARANJA),
        fio('driver', 'COM', 'motor', 'vermelho', VERMELHO),
    ]
    codigo = [
        '// O 28BYJ-48 pelo ULN2003: meia volta para cada lado.',
        '#include <Stepper.h>',
        '',
        'const int PASSOS_POR_VOLTA = 2048;',
        'Stepper motor(PASSOS_POR_VOLTA, 8, 10, 9, 11);   // a ordem IN1-IN3-IN2-IN4',
        '',
        'void setup() {',
        '  Serial.begin(9600);',
        '  motor.setSpeed(12);',
        '}',
        '',
        'void loop() {',
        '  Serial.println("meia volta no sentido horario");',
        '  motor.step(PASSOS_POR_VOLTA / 2);',
        '  delay(500);',
        '  Serial.println("meia volta no anti-horario");',
        '  motor.step(-PASSOS_POR_VOLTA / 2);',
        '  delay(500);',
        '}',
    ]
    roteiro = [
        confere('o motor gira (mais de 90 graus)', 6, passo_min={'peca': 'motor', 'valor': 90}),
        confere('e volta', 6, serial='anti-horario'),
    ]
    projeto('motor-de-passo-28byj48', 'Motor de passo 28BYJ-48 com ULN2003 (Uno)', [
        'O motor de passo dos kits (2048 passos por volta, com a reducao) pelo',
        'ULN2003, com a biblioteca Stepper: meia volta para cada lado.',
    ], codigo, pecas, fios, roteiro)


def ventilador():
    pecas = [peca('placa', 'arduino-uno', 40, 220, Usb=True), peca('ponte', 'l298n', 380, 220),
             peca('motor', 'motor-dc', 620, 220), peca('pot', 'potentiometer', 380, 460, Giro=0.2),
             bateria('pilhas', 6, 2000, 2500, '4 pilhas AA (6 V)', 620, 420)]
    hx, hy = no_eixo('helice-130', 'motor-dc', 620, 220)
    pecas.append(peca('helice', 'helice-130', hx, hy))
    fios = [
        fio('pilhas', '+', 'ponte', '12V', VERMELHO), fio('pilhas', '-', 'ponte', 'GND', PRETO),
        fio('ponte', 'GND', 'placa', 'GND.2', PRETO), fio('placa', '5V', 'ponte', '5V', VERMELHO),
        fio('placa', '9', 'ponte', 'ENA', AMARELO), fio('placa', '7', 'ponte', 'IN1', VERDE), fio('placa', '8', 'ponte', 'IN2', AZUL),
        fio('ponte', 'OUT1', 'motor', '+', VERMELHO), fio('ponte', 'OUT2', 'motor', '-', PRETO),
        fio('pot', 'VCC', 'placa', '5V', VERMELHO), fio('pot', 'GND', 'placa', 'GND.3', PRETO), fio('pot', 'SIG', 'placa', 'A0', AMARELO),
    ]
    codigo = [
        '// Ventilador: o potenciometro regula a velocidade do motor 130 pela L298N.',
        'void setup() {',
        '  Serial.begin(9600);',
        '  pinMode(9, OUTPUT);',
        '  pinMode(7, OUTPUT);',
        '  pinMode(8, OUTPUT);',
        '  digitalWrite(7, HIGH);   // sempre no mesmo sentido',
        '  digitalWrite(8, LOW);',
        '}',
        '',
        'void loop() {',
        '  int velocidade = analogRead(A0) / 4;',
        '  analogWrite(9, velocidade);',
        '  delay(50);',
        '}',
    ]
    roteiro = [
        # O 130 so sai do lugar com ~2 V (a ficha da Adafruit): 20% do PWM nao basta.
        confere('com o potenciometro em 20% o motor nem sai do lugar (precisa de ~2 V)', 4, rpm_max=['motor'], valor=50),
        mexer('pot', giro=0.5),
        confere('na metade ele gira devagar', 4, rpm_min=['motor'], valor=1500),
        confere('(e nao no maximo)', 1, rpm_max=['motor'], valor=3000),
        mexer('pot', giro=1.0),
        confere('no maximo, bem mais rapido', 4, rpm_min=['motor'], valor=4000),
        confere('e a helice empurra', 2, empuxo_min=['motor'], valor=1),
    ]
    projeto('ventilador-l298n', 'Ventilador com controle de velocidade (L298N, Uno)', [
        'O motor 130 com a helice, pela ponte H L298N, nas 4 pilhas AA: o',
        'potenciometro no A0 vira o PWM do ENA (a velocidade).',
        '',
        'O Uno esta no cabo USB (na barra da placa: USB -> cabo ligado): 6 V',
        'no VIN nao bastam para ele (o regulador pede 7 V), e as pilhas sao so',
        'do motor.',
    ], codigo, pecas, fios, roteiro)


def balanca():
    pecas = [peca('placa', 'arduino-uno', 40, 260), peca('celula', 'hx711', 380, 40, Giro=0.0), peca('lcd', 'lcd1602-i2c', 520, 260)]
    fios = [
        fio('celula', 'VCC', 'placa', '5V', VERMELHO), fio('celula', 'GND', 'placa', 'GND.2', PRETO),
        fio('celula', 'DT', 'placa', '3', AZUL), fio('celula', 'SCK', 'placa', '2', VERDE),
        fio('lcd', 'VCC', 'placa', '5V', VERMELHO), fio('lcd', 'GND', 'placa', 'GND.3', PRETO),
        fio('lcd', 'SDA', 'placa', 'A4', AZUL), fio('lcd', 'SCL', 'placa', 'A5', VERDE),
    ]
    codigo = [
        '// Balanca: a celula de carga pelo HX711, o peso no LCD.',
        '#include <HX711.h>',
        '#include <Wire.h>',
        '#include <LiquidCrystal_I2C.h>',
        '',
        'HX711 balanca;',
        'LiquidCrystal_I2C lcd(0x27, 16, 2);',
        '',
        'void setup() {',
        '  balanca.begin(3, 2);',
        '  balanca.set_scale(420.0);',
        '  balanca.tare();   // a celula vazia vira o zero',
        '  lcd.init();',
        '  lcd.backlight();',
        '}',
        '',
        'void loop() {',
        '  float gramas = balanca.get_units(5);',
        '  lcd.setCursor(0, 0);',
        '  lcd.print("Peso: ");',
        '  lcd.print(gramas, 0);',
        '  lcd.print(" g     ");',
        '  delay(300);',
        '}',
    ]
    roteiro = [
        confere('vazia (depois do tare), o LCD mostra 0 g', 5, lcd='Peso: 0 g'),
        mexer('celula', giro=0.1),
        confere('com 500 g, o LCD mostra os 500 g', 5, lcd='Peso: 500 g'),
        mexer('celula', giro=0.25),
        confere('com 1250 g na celula, o LCD acompanha', 5, lcd='Peso: 1250 g'),
    ]
    projeto('balanca-hx711-lcd', 'Balanca com HX711 e LCD I2C (Uno)', [
        'A celula de carga pelo amplificador HX711 (DT no 3, SCK no 2) e o peso',
        'no LCD 16x2 I2C. Mude o peso na barra da celula.',
        '',
        'O tare() no setup faz da celula vazia o zero: sem ele, o HX711 mostra',
        'o peso da propria plataforma (~200 g aqui), como na bancada.',
    ], codigo, pecas, fios, roteiro)


def bluetooth_led():
    pecas = [peca('placa', 'esp32-devkit-v1', 40, 40)]
    p, f = led('led', 'verde', 280, 40, 'D2', 'GND.1')
    codigo = [
        '// O celular manda "liga" ou "desliga" pelo Bluetooth classico.',
        '#include <BluetoothSerial.h>',
        'BluetoothSerial SerialBT;',
        '',
        'void setup() {',
        '  Serial.begin(115200);',
        '  pinMode(2, OUTPUT);',
        '  SerialBT.begin("RoboESP32");',
        '  Serial.println("Bluetooth pronto: conecte em RoboESP32");',
        '}',
        '',
        'void loop() {',
        '  if (SerialBT.available()) {',
        "    String comando = SerialBT.readStringUntil('\\n');",
        '    comando.trim();',
        '    if (comando == "liga") { digitalWrite(2, HIGH); SerialBT.println("LED aceso"); }',
        '    else if (comando == "desliga") { digitalWrite(2, LOW); SerialBT.println("LED apagado"); }',
        '    Serial.println("recebi: " + comando);',
        '  }',
        '}',
    ]
    roteiro = [
        confere('o Bluetooth anuncia', 4, serial='Bluetooth pronto'),
        {'celular_conectar': 'RoboESP32'},
        esperar(0.8),
        {'celular_enviar': 'liga'},
        confere('o celular manda "liga"', 4, serial='recebi: liga'),
        confere('e o LED acende', 2, led={'peca': 'led', 'aceso': True}),
    ]
    projeto('bluetooth-led-esp32', 'LED pelo Bluetooth do celular (ESP32)', [
        'A ESP32 abre uma porta serial Bluetooth (BluetoothSerial) chamada',
        'RoboESP32. No celular do simulador (aba BLUETOOTH), conecte e mande',
        '"liga" ou "desliga".',
    ], codigo, pecas + p, f, roteiro)


def espnow():
    pecas = [peca('placa', 'esp32-devkit-v1', 40, 40), peca('placa2', 'esp32-devkit-v1', 520, 40)]
    pb, fb = botao('botao', 260, 120, 'D4', 'GND.1')
    p, f = led('led', 'vermelho', 760, 60, 'D2', 'GND.1', placa='placa2')
    enviar = [
        '// ESP-NOW, quem manda: o botao no GPIO4.',
        '#include <esp_now.h>',
        '#include <WiFi.h>',
        '',
        'uint8_t paraTodos[] = {0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF};',
        'esp_now_peer_info_t par;',
        'int antes = HIGH;',
        '',
        'void setup() {',
        '  Serial.begin(115200);',
        '  pinMode(4, INPUT_PULLUP);',
        '  WiFi.mode(WIFI_STA);',
        '  esp_now_init();',
        '  memcpy(par.peer_addr, paraTodos, 6);',
        '  par.channel = 0;',
        '  par.encrypt = false;',
        '  esp_now_add_peer(&par);',
        '  Serial.println("emissor pronto");',
        '}',
        '',
        'void loop() {',
        '  int agora = digitalRead(4);',
        '  if (agora != antes) {',
        '    uint8_t apertado = agora == LOW;',
        '    esp_now_send(paraTodos, &apertado, 1);',
        '    Serial.println(apertado ? "mandei: apertado" : "mandei: solto");',
        '    antes = agora;',
        '  }',
        '  delay(10);',
        '}',
    ]
    receber = [
        '// ESP-NOW, quem recebe: o LED no GPIO2 segue o botao da outra placa.',
        '#include <esp_now.h>',
        '#include <WiFi.h>',
        '',
        'void aoReceber(const esp_now_recv_info_t *info, const uint8_t *dados, int tamanho) {',
        '  digitalWrite(2, dados[0] ? HIGH : LOW);',
        '  Serial.println(dados[0] ? "recebi: apertado" : "recebi: solto");',
        '}',
        '',
        'void setup() {',
        '  Serial.begin(115200);',
        '  pinMode(2, OUTPUT);',
        '  WiFi.mode(WIFI_STA);',
        '  esp_now_init();',
        '  esp_now_register_recv_cb(aoReceber);',
        '  Serial.println("receptor pronto");',
        '}',
        '',
        'void loop() {',
        '  delay(100);',
        '}',
    ]
    pecas[0]['Codigo'] = enviar
    pecas[1]['Codigo'] = receber
    roteiro = [
        confere('as duas placas iniciam', 4, serial='receptor pronto', placa='placa2'),
        esperar(0.5),
        apertar('botao'),
        confere('o botao de uma placa chega na outra pelo ESP-NOW', 3, serial='recebi: apertado', placa='placa2'),
        confere('e acende o LED dela', 2, led={'peca': 'led', 'aceso': True}),
        soltar('botao'),
        confere('soltou: a primeira placa manda o solto', 3, serial='mandei: solto'),
        confere('e a segunda recebe', 3, serial='recebi: solto', placa='placa2'),
        confere('soltou: apaga', 3, led={'peca': 'led', 'aceso': False}),
    ]
    projeto('espnow-botao-led', 'Botao numa ESP32, LED na outra (ESP-NOW)', [
        'Duas ESP32 sem roteador nenhum: a da esquerda manda o estado do botao',
        'pelo ESP-NOW (para todas, FF:FF:FF:FF:FF:FF), a da direita acende o',
        'LED. Cada placa tem o proprio sketch (clique nela para ver).',
    ], enviar, pecas + pb + p, fb + f, roteiro)


def servidor_web():
    pecas = [peca('placa', 'esp32-devkit-v1', 40, 40)]
    p, f = led('led', 'amarelo', 280, 40, 'D2', 'GND.1')
    codigo = [
        '// Servidor web na ESP32: /liga e /desliga acendem o LED.',
        '#include <WiFi.h>',
        '#include <WebServer.h>',
        '',
        'WebServer servidor(80);',
        '',
        'void setup() {',
        '  Serial.begin(115200);',
        '  pinMode(2, OUTPUT);',
        '  WiFi.begin("CircuitSketch-WiFi", "12345678");',
        '  while (WiFi.status() != WL_CONNECTED) delay(300);',
        '  Serial.print("IP da placa: ");',
        '  Serial.println(WiFi.localIP());',
        '  servidor.on("/", []() { servidor.send(200, "text/html", "<a href=/liga>liga</a> | <a href=/desliga>desliga</a>"); });',
        '  servidor.on("/liga", []() { digitalWrite(2, HIGH); servidor.send(200, "text/plain", "LED aceso"); });',
        '  servidor.on("/desliga", []() { digitalWrite(2, LOW); servidor.send(200, "text/plain", "LED apagado"); });',
        '  servidor.begin();',
        '  Serial.println("servidor pronto");',
        '}',
        '',
        'void loop() {',
        '  servidor.handleClient();',
        '  delay(2);',
        '}',
    ]
    roteiro = [
        confere('a ESP32 conecta no WiFi', 8, serial='IP da placa'),
        confere('e o servidor abre', 3, serial='servidor pronto'),
    ]
    projeto('servidor-web-led', 'Servidor web com LED (ESP32)', [
        'A ESP32 conecta no WiFi do simulador e abre um servidor web: /liga e',
        '/desliga acendem e apagam o LED. O endereco aparece no painel REDE.',
    ], codigo, pecas + p, f, roteiro)


def sono_profundo():
    pecas = [peca('placa', 'esp32-devkit-v1', 40, 40)]
    pb, fb = botao('botao', 260, 120, 'D4', 'GND.1')
    codigo = [
        '// Sono profundo: a ESP32 dorme e acorda pelo botao no GPIO4.',
        'RTC_DATA_ATTR int vezes = 0;',
        '',
        'void setup() {',
        '  Serial.begin(115200);',
        '  vezes++;',
        '  if (esp_sleep_get_wakeup_cause() == ESP_SLEEP_WAKEUP_EXT0) Serial.println("acordou pelo botao");',
        '  Serial.print("ligou ");',
        '  Serial.print(vezes);',
        '  Serial.println(" vez(es)");',
        '  pinMode(4, INPUT_PULLUP);',
        '  esp_sleep_enable_ext0_wakeup(GPIO_NUM_4, 0);',
        '  delay(1000);',
        '  Serial.println("dormindo...");',
        '  esp_deep_sleep_start();',
        '}',
        '',
        'void loop() {',
        '}',
    ]
    roteiro = [
        confere('a ESP32 vai dormir', 4, serial='dormindo'),
        esperar(0.5),
        apertar('botao'),
        confere('o botao acorda', 4, serial='acordou pelo botao'),
        soltar('botao'),
        confere('e a memoria RTC lembra quantas vezes ligou', 2, serial='ligou 2 vez'),
    ]
    projeto('sono-profundo-botao', 'Sono profundo acordado por botao (ESP32)', [
        'A ESP32 conta quantas vezes ligou (RTC_DATA_ATTR guarda o numero no',
        'sono) e dorme; o botao no GPIO4 (ext0) acorda.',
    ], codigo, pecas + pb, fb, roteiro)


def nrf24():
    pecas = [peca('placa', 'arduino-uno', 40, 220), peca('radio1', 'nrf24l01', 380, 40),
             peca('placa2', 'arduino-uno', 600, 220), peca('radio2', 'nrf24l01', 940, 40)]
    fios = []
    for placa, radio in [('placa', 'radio1'), ('placa2', 'radio2')]:
        fios += [fio(radio, 'VCC', placa, '3.3V', VERMELHO), fio(radio, 'GND', placa, 'GND.2', PRETO),
                 fio(radio, 'CE', placa, '9', AMARELO), fio(radio, 'CSN', placa, '10', LARANJA),
                 fio(radio, 'SCK', placa, '13', VERDE), fio(radio, 'MOSI', placa, '11', AZUL), fio(radio, 'MISO', placa, '12', ROXO)]
    emissor = [
        '// nRF24L01, o emissor: um contador a cada meio segundo.',
        '#include <SPI.h>',
        '#include <RF24.h>',
        '',
        'RF24 radio(9, 10);',
        'const byte endereco[6] = "00001";',
        'int contador = 0;',
        '',
        'void setup() {',
        '  Serial.begin(9600);',
        '  radio.begin();',
        '  radio.openWritingPipe(endereco);',
        '  radio.stopListening();',
        '}',
        '',
        'void loop() {',
        '  contador++;',
        '  radio.write(&contador, sizeof(contador));',
        '  Serial.print("enviei ");',
        '  Serial.println(contador);',
        '  delay(500);',
        '}',
    ]
    receptor = [
        '// nRF24L01, o receptor.',
        '#include <SPI.h>',
        '#include <RF24.h>',
        '',
        'RF24 radio(9, 10);',
        'const byte endereco[6] = "00001";',
        '',
        'void setup() {',
        '  Serial.begin(9600);',
        '  radio.begin();',
        '  radio.openReadingPipe(0, endereco);',
        '  radio.startListening();',
        '}',
        '',
        'void loop() {',
        '  if (radio.available()) {',
        '    int valor = 0;',
        '    radio.read(&valor, sizeof(valor));',
        '    Serial.print("recebido ");',
        '    Serial.println(valor);',
        '  }',
        '}',
    ]
    pecas[0]['Codigo'] = emissor
    pecas[2]['Codigo'] = receptor
    roteiro = [
        confere('o emissor manda', 4, serial='enviei'),
        confere('o receptor recebe pelo radio de 2,4 GHz', 5, serial='recebido', placa='placa2'),
    ]
    projeto('nrf24l01-duas-placas', 'Dois Unos conversando por nRF24L01', [
        'Dois modulos de radio nRF24L01 (SPI, biblioteca RF24): um Uno manda um',
        'contador a cada meio segundo, o outro recebe e mostra no monitor.',
        'O nRF24 e de 3,3 V: o VCC vai no 3.3V do Uno.',
    ], emissor, pecas, fios, roteiro)


def lcd_paralelo():
    pecas = [peca('placa', 'arduino-uno', 40, 260), peca('lcd', 'lcd1602', 380, 40)]
    fios = [fio('lcd', 'VSS', 'placa', 'GND.2', PRETO), fio('lcd', 'VDD', 'placa', '5V', VERMELHO),
            fio('lcd', 'RS', 'placa', '12', AMARELO), fio('lcd', 'RW', 'placa', 'GND.2', PRETO), fio('lcd', 'E', 'placa', '11', LARANJA),
            fio('lcd', 'D4', 'placa', '5', AZUL), fio('lcd', 'D5', 'placa', '4', AZUL), fio('lcd', 'D6', 'placa', '3', AZUL), fio('lcd', 'D7', 'placa', '2', AZUL),
            fio('lcd', 'A', 'placa', '5V', VERMELHO), fio('lcd', 'K', 'placa', 'GND.3', PRETO)]
    codigo = [
        '// O LCD 16x2 sem modulo I2C: 6 fios de dados (o exemplo HelloWorld).',
        '#include <LiquidCrystal.h>',
        '',
        'LiquidCrystal lcd(12, 11, 5, 4, 3, 2);',
        '',
        'void setup() {',
        '  lcd.begin(16, 2);',
        '  lcd.print("Ola, mundo!");',
        '}',
        '',
        'void loop() {',
        '  lcd.setCursor(0, 1);',
        '  lcd.print(millis() / 1000);',
        '  lcd.print(" s");',
        '  delay(200);',
        '}',
    ]
    roteiro = [confere('o LCD escreve', 4, lcd='Ola, mundo!'), confere('e conta os segundos', 4, lcd='2 s')]
    projeto('lcd-paralelo', 'LCD 16x2 sem I2C (LiquidCrystal, Uno)', [
        'O LCD 16x2 ligado direto (RS, E e D4 a D7), como no exemplo HelloWorld',
        'da Arduino IDE: escreve e conta os segundos.',
    ], codigo, pecas, fios, roteiro)


def pico_micropython():
    pecas = [peca('placa', 'pi-pico', 40, 40)]
    p, f = led('led', 'verde', 260, 40, 'GP15', 'GND.4')
    pb, fb = botao('botao', 260, 220, 'GP14', 'GND.5')
    codigo = [
        '# MicroPython na Raspberry Pi Pico: o botao no GP14 acende o LED no GP15.',
        'from machine import Pin',
        'import time',
        '',
        'led = Pin(15, Pin.OUT)',
        'botao = Pin(14, Pin.IN, Pin.PULL_UP)',
        '',
        'print("Pico pronta")',
        'while True:',
        '    led.value(1 if botao.value() == 0 else 0)',
        '    time.sleep_ms(20)',
    ]
    roteiro = [
        confere('a Pico roda o MicroPython', 4, serial='Pico pronta'),
        apertar('botao'),
        confere('com o botao apertado o LED acende', 2, led={'peca': 'led', 'aceso': True}),
        soltar('botao'),
        confere('solto, apaga', 2, led={'peca': 'led', 'aceso': False}),
    ]
    projeto('pico-micropython-botao', 'Botao e LED em MicroPython (Raspberry Pi Pico)', [
        'O mesmo botao-e-LED em MicroPython, na Raspberry Pi Pico: o Pin com',
        'PULL_UP le 0 com o botao apertado.',
    ], codigo, pecas + p + pb, f + fb, roteiro)


# =====================================================================
#  6) COMPONENTES NOVOS (ALPHA 24)
# =====================================================================

def npn_motor_esp32():
    pecas = [
        peca('placa', 'esp32-devkit-v1', 40, 200, Usb=True),
        bateria('pilhas', 6, 2000, 2500, '4 pilhas AA (6 V)', 520, 420),
        peca('q', 'transistor-npn', 400, 200, Modelo='2n2222a'),
        peca('rb', 'resistor', 260, 200, Ohms=1000),
        peca('motor', 'motor-dc', 600, 200),
        peca('diodo', 'diodo', 600, 80, Modelo='1n4007'),
    ]
    fios = [
        fio('placa', 'D13', 'rb', '1', VERDE),
        fio('rb', '2', 'q', 'B', VERDE),
        fio('q', 'E', 'pilhas', '-', PRETO),
        fio('pilhas', '-', 'placa', 'GND.1', PRETO),
        fio('pilhas', '+', 'motor', '+', VERMELHO),
        fio('motor', '+', 'diodo', 'C', VERMELHO),
        fio('motor', '-', 'diodo', 'A', PRETO),
        fio('motor', '-', 'q', 'C', PRETO),
    ]
    codigo = [
        '// Acionamento de motor DC com transistor NPN 2N2222A e ESP32.',
        '// Diodo 1N4007 de roda livre em paralelo reverso com o motor.',
        'const int PINO_MOTOR = 13;',
        '',
        'void setup() {',
        '  pinMode(PINO_MOTOR, OUTPUT);',
        '}',
        '',
        'void loop() {',
        '  digitalWrite(PINO_MOTOR, HIGH);',
        '  delay(2500);',
        '  digitalWrite(PINO_MOTOR, LOW);',
        '  delay(7000);',
        '}',
    ]
    roteiro = [
        confere('com o pino em HIGH o motor gira', 3, rpm_min=['motor'], valor=500),
        confere('sem avisos no circuito', 1, sem_avisos=True),
        esperar(2.8),
        confere('com o pino em LOW o motor para', 3, rpm_max=['motor'], valor=50),
    ]
    projeto('npn-motor-esp32', 'Motor DC acionado por transistor NPN (ESP32, 2N2222A)', [
        'A ESP32 controla um motor DC de 6 V atraves de um transistor NPN 2N2222A,',
        'com resistor de 1 kohm na base e diodo 1N4007 de roda livre.',
    ], codigo, pecas, fios, roteiro)


def mosfet_fita_uno():
    pecas = [
        peca('placa', 'arduino-uno', 40, 200, Usb=True),
        peca('pot', 'potentiometer', 40, 440, Giro=0.15),
        bateria('bat', 12, 10000, 2000, 'Bateria 12 V', 520, 420),
        peca('mosfet', 'mosfet-n', 380, 220, Modelo='irlz44n'),
        peca('r_led', 'resistor', 480, 80, Ohms=470),
        peca('led', 'led', 580, 80, Cor='amarelo'),
    ]
    fios = [
        fio('pot', 'VCC', 'placa', '5V', VERMELHO),
        fio('pot', 'GND', 'placa', 'GND.2', PRETO),
        fio('pot', 'SIG', 'placa', 'A0', AMARELO),
        fio('placa', '9', 'mosfet', 'G', LARANJA),
        fio('mosfet', 'S', 'bat', '-', PRETO),
        fio('bat', '-', 'placa', 'GND.1', PRETO),
        fio('bat', '+', 'r_led', '1', VERMELHO),
        fio('r_led', '2', 'led', 'A', VERMELHO),
        fio('led', 'C', 'mosfet', 'D', PRETO),
    ]
    codigo = [
        '// Controle de carga de 12 V com MOSFET IRLZ44N e PWM no Arduino Uno.',
        '// O potenciometro no A0 ajusta o ciclo de trabalho no pino 9.',
        'const int PINO_MOSFET = 9;',
        '',
        'void setup() {',
        '  pinMode(PINO_MOSFET, OUTPUT);',
        '}',
        '',
        'void loop() {',
        '  int leitura = analogRead(A0);',
        '  analogWrite(PINO_MOSFET, leitura / 4);',
        '  delay(20);',
        '}',
    ]
    roteiro = [
        confere('com PWM baixo o brilho e fraco', 3, brilho={'peca': 'led', 'max': 0.35}),
        mexer('pot', giro=0.95),
        confere('com PWM alto o brilho e forte', 3, brilho={'peca': 'led', 'min': 0.70}),
        confere('sem avisos', 1, sem_avisos=True),
    ]
    projeto('mosfet-fita-uno', 'Controle de carga 12 V com MOSFET IRLZ44N (Uno, PWM)', [
        'O Arduino Uno modula uma carga de 12 V (LED com resistor) usando PWM',
        'atraves de um MOSFET de potencia canal N de nivel logico (IRLZ44N).',
    ], codigo, pecas, fios, roteiro)


def zener_referencia():
    pecas = [
        peca('placa', 'arduino-uno', 40, 200, Usb=True),
        bateria('bat', 9, 20000, 1000, 'Bateria 9 V', 400, 420),
        peca('r_z', 'resistor', 400, 140, Ohms=470),
        peca('zener', 'diodo-zener', 520, 200, Modelo='5v1'),
        peca('r1', 'resistor', 320, 240, Ohms=10000),
        peca('r2', 'resistor', 320, 320, Ohms=10000),
    ]
    fios = [
        fio('bat', '+', 'r_z', '1', VERMELHO),
        fio('r_z', '2', 'zener', 'C', VERMELHO),
        fio('zener', 'A', 'bat', '-', PRETO),
        fio('bat', '-', 'placa', 'GND.1', PRETO),
        fio('zener', 'C', 'r1', '1', AZUL),
        fio('r1', '2', 'r2', '1', AMARELO),
        fio('r2', '2', 'placa', 'GND.2', PRETO),
        fio('placa', 'A0', 'r1', '2', AMARELO),
    ]
    codigo = [
        '// Referencia de tensao com diodo Zener de 5,1 V.',
        '// O divisor de tensao (10k / 10k) divide por 2 para leitura segura no ADC.',
        'void setup() {',
        '  Serial.begin(9600);',
        '}',
        '',
        'void loop() {',
        '  int leitura = analogRead(A0);',
        '  float v = (leitura * 5.0 / 1023.0) * 2.0;',
        '  Serial.print("Zener: ");',
        '  Serial.print(v, 1);',
        '  Serial.println(" V");',
        '  delay(300);',
        '}',
    ]
    roteiro = [
        confere('a tensao do zener e lida no monitor (~5,2 V)', 4, serial='Zener: 5.2 V'),
        confere('sem avisos', 1, sem_avisos=True),
    ]
    projeto('zener-referencia', 'Referencia de tensao com diodo Zener de 5,1 V (Uno)', [
        'Diodo Zener de 5,1 V polarizado por resistor de 470 ohms numa bateria de 9 V.',
        'Um divisor resistivo 10k/10k divide a tensao pela metade para medicao precisa no A0.',
    ], codigo, pecas, fios, roteiro)


def capacitor_rc_led():
    pecas = [
        peca('placa', 'arduino-uno', 40, 200, Usb=True),
        peca('r_carga', 'resistor', 240, 160, Ohms=330),
        peca('diodo', 'diodo', 340, 160, Modelo='1n4148'),
        peca('cap', 'capacitor-eletrolitico', 440, 220, Modelo='1000u-16v'),
        peca('r_led', 'resistor', 520, 160, Ohms=330),
        peca('led', 'led', 600, 160, Cor='verde'),
    ]
    fios = [
        fio('placa', '13', 'r_carga', '1', VERDE),
        fio('r_carga', '2', 'diodo', 'A', VERDE),
        fio('diodo', 'C', 'cap', '+', VERDE),
        fio('cap', '+', 'r_led', '1', VERDE),
        fio('r_led', '2', 'led', 'A', VERDE),
        fio('led', 'C', 'cap', '-', PRETO),
        fio('cap', '-', 'placa', 'GND.1', PRETO),
    ]
    codigo = [
        '// Circuito RC: o capacitor carrega com o pino 13 em HIGH.',
        '// O diodo 1N4148 impede retorno ao pino quando vai a LOW;',
        '// o capacitor descarrega pelo resistor e o LED apaga devagar.',
        'const int PINO = 13;',
        '',
        'void setup() {',
        '  pinMode(PINO, OUTPUT);',
        '}',
        '',
        'void loop() {',
        '  digitalWrite(PINO, HIGH);',
        '  delay(2500);',
        '  digitalWrite(PINO, LOW);',
        '  delay(8000);',
        '}',
    ]
    roteiro = [
        confere('com pino em HIGH o capacitor carrega e o LED acende', 3, led={'peca': 'led', 'aceso': True}),
        confere('sem avisos', 1, sem_avisos=True),
        esperar(2.8),
        confere('logo apos o pino ir a LOW o capacitor mantem o LED aceso', 1, led={'peca': 'led', 'aceso': True}),
        esperar(2.5),
        confere('apos descarregar o LED apaga', 2, led={'peca': 'led', 'aceso': False}),
    ]
    projeto('capacitor-rc-led', 'Carga e descarga RC com capacitor eletrolitico e LED (Uno)', [
        'O pino 13 carrega um capacitor eletrolitico de 1000 uF.',
        'Ao ir para LOW, a carga acumulada mantem o LED aceso por algum tempo antes de apagar.',
    ], codigo, pecas, fios, roteiro)


def opto_isolado():
    pecas = [
        peca('placa', 'arduino-uno', 40, 200, Usb=True),
        peca('botao', 'pushbutton', 40, 420),
        peca('r_opto', 'resistor', 240, 200, Ohms=330),
        peca('opto', 'optoacoplador', 340, 260),
        bateria('bat', 9, 20000, 1000, 'Bateria 9 V', 580, 420),
        peca('r_led', 'resistor', 480, 200, Ohms=470),
        peca('led', 'led', 580, 200, Cor='verde'),
    ]
    fios = [
        fio('placa', '2', 'botao', '1.l', AZUL),
        fio('botao', '2.l', 'placa', 'GND.1', PRETO),
        fio('placa', '13', 'r_opto', '1', VERDE),
        fio('r_opto', '2', 'opto', 'A', VERDE),
        fio('opto', 'K', 'placa', 'GND.2', PRETO),
        fio('bat', '+', 'r_led', '1', VERMELHO),
        fio('r_led', '2', 'led', 'A', VERMELHO),
        fio('led', 'C', 'opto', 'C', PRETO),
        fio('opto', 'E', 'bat', '-', PRETO),
    ]
    codigo = [
        '// Isolamento galvanico com optoacoplador PC817.',
        '// O botao no pino 2 aciona o LED interno do opto (pino 13).',
        '// O fototransistor conduz e acende o LED no circuito isolado.',
        'const int BOTAO = 2, OPTO = 13;',
        '',
        'void setup() {',
        '  pinMode(BOTAO, INPUT_PULLUP);',
        '  pinMode(OPTO, OUTPUT);',
        '}',
        '',
        'void loop() {',
        '  digitalWrite(OPTO, digitalRead(BOTAO) == LOW ? HIGH : LOW);',
        '  delay(20);',
        '}',
    ]
    roteiro = [
        confere('inicialmente o LED isolado esta apagado', 2, led={'peca': 'led', 'aceso': False}),
        apertar('botao'),
        confere('com o botao apertado o LED isolado acende', 2, led={'peca': 'led', 'aceso': True}),
        soltar('botao'),
        confere('soltando o botao o LED isolado apaga', 2, led={'peca': 'led', 'aceso': False}),
        confere('sem avisos', 1, sem_avisos=True),
    ]
    projeto('opto-isolado', 'Circuito isolado com optoacoplador PC817 (Uno)', [
        'Isolamento completo entre o Arduino Uno e o circuito de carga.',
        'O aperto do botao aciona o optoacoplador, acendendo o LED alimentado por bateria.',
    ], codigo, pecas, fios, roteiro)


def fusivel_curto():
    pecas = [
        peca('placa', 'arduino-uno', 40, 200, Usb=True),
        bateria('bat', 12, 20000, 2000, 'Bateria 12 V', 380, 420),
        peca('fusivel', 'fusivel', 460, 240, Modelo='1a'),
        peca('r_carga', 'resistor', 580, 240, Ohms=470),
        peca('led', 'led', 660, 240, Cor='vermelho'),
        peca('botao', 'pushbutton', 460, 360),
    ]
    fios = [
        fio('bat', '+', 'fusivel', '1', VERMELHO),
        fio('fusivel', '2', 'r_carga', '1', VERMELHO),
        fio('r_carga', '2', 'led', 'A', VERMELHO),
        fio('led', 'C', 'bat', '-', PRETO),
        fio('bat', '-', 'placa', 'GND.1', PRETO),
        fio('fusivel', '2', 'botao', '1.l', VERMELHO),
        fio('botao', '2.l', 'bat', '-', PRETO),
    ]
    codigo = [
        '// Circuito de protecao contra curto-circuito com fusivel de 1 A.',
        '// A carga normal (LED com resistor em 12 V) consome ~20 mA.',
        '// O botao de teste coloca a linha em curto com o terra;',
        '// a corrente excede a capacidade do fusivel, que queima e abre o circuito.',
        'void setup() {',
        '  Serial.begin(9600);',
        '}',
        '',
        'void loop() {',
        '  delay(100);',
        '}',
    ]
    roteiro = [
        confere('em condicoes normais o LED acende', 2, led={'peca': 'led', 'aceso': True}),
        confere('sem avisos em regime normal', 1, sem_avisos=True),
        apertar('botao'),
        esperar(0.5),
        confere('o curto queima o fusivel e gera aviso', 3, serial='abriu'),
        soltar('botao'),
        confere('com o fusivel aberto o LED apaga', 2, led={'peca': 'led', 'aceso': False}),
    ]
    projeto('fusivel-curto', 'Protecao contra curto-circuito com fusivel 1 A', [
        'Uma bateria de 12 V alimenta um LED atraves de um fusivel de 1 A.',
        'Ao pressionar o botao de teste, um curto-circuito queima o fusivel e corta a corrente.',
    ], codigo, pecas, fios, roteiro)


def schottky_protecao():
    pecas = [
        bateria('bat', 9, 20000, 1000, 'Bateria 9 V', 80, 200),
        peca('diodo', 'diodo-schottky', 240, 140, Modelo='1n5819'),
        peca('r', 'resistor', 340, 200, Ohms=330),
        peca('led', 'led', 440, 200, Cor='verde'),
    ]
    fios = [
        fio('bat', '+', 'diodo', 'A', VERMELHO),
        fio('diodo', 'C', 'r', '1', VERMELHO),
        fio('r', '2', 'led', 'A', VERMELHO),
        fio('led', 'C', 'bat', '-', PRETO),
    ]
    codigo = [
        '// Protecao contra inversao de polaridade com diodo Schottky 1N5819.',
        '// Com bateria normal, conduz com baixa queda (~0,3 V) e o LED acende.',
        '// Com bateria invertida, o diodo bloqueia a corrente e evita danos.',
    ]
    roteiro = [
        confere('com polaridade normal o LED acende', 2, led={'peca': 'led', 'aceso': True}),
        confere('sem avisos na polaridade normal', 1, sem_avisos=True),
        mexer('bat', volts=-9.0),
        confere('com bateria invertida o LED apaga', 2, led={'peca': 'led', 'aceso': False}),
        confere('e nada queima com a inversao', 1, sem_avisos=True),
        mexer('bat', volts=9.0),
        confere('com polaridade restabelecida o LED volta a acender', 2, led={'peca': 'led', 'aceso': True}),
    ]
    projeto('schottky-protecao', 'Protecao contra polaridade reversa (Diodo Schottky)', [
        'Diodo Schottky 1N5819 em serie com a alimentacao de 9 V.',
        'Permite a passagem de corrente com queda minima e bloqueia polaridade invertida.',
    ], codigo, pecas, fios, roteiro)


def pcb_semaforo():
    """
    O semaforo soldado numa PCB perfurada de verdade: os resistores e os LEDs
    ESPETADOS nos furos (as pernas caem no furo pela posicao, sem fio), e as
    ligacoes de furo a furo pelo lado de baixo -- a trilha de estanho
    (Fio.Lado = "baixo"), que aparece com a placa virada. Do Uno para a placa
    vao so os fios de fora: os tres pinos e o GND.
    """
    PASSO = 2.54 * 96 / 25.4
    PX, PY = 360, 100                                  # a PCB 18x24
    def furo(c, l):
        return PX + PASSO * c, PY + PASSO * l
    # pernas no desenho do catalogo (pecas.json)
    R_1 = (0, 5.65)                                    # resistor: o 1 na esquerda, o 2 seis furos adiante
    LED_C = (15, 42)                                   # LED: o catodo a esquerda do anodo (um furo)
    def espetar(id_, tipo, perna, c, l, **extra):
        x, y = furo(c, l)
        return peca(id_, tipo, x - perna[0], y - perna[1], **extra)
    def trilha(a, b):
        return {**fio('pcb', a, 'pcb', b, '#C3C7CE'), 'Lado': 'baixo', 'Estilo': 'reto'}

    pecas = [peca('placa', 'arduino-uno', 40, 200, Usb=True), peca('pcb', 'pcb-18x24', PX, PY)]
    fios = []
    for (letra, cor, pino, linha) in [('v', 'vermelho', '13', 6), ('a', 'amarelo', '12', 12), ('g', 'verde', '11', 18)]:
        # o resistor do x3 ao x9, o LED com o catodo no x11 e o anodo no x12
        pecas.append(espetar(f'r_{letra}', 'resistor', R_1, 3, linha, Ohms=220))
        pecas.append(espetar(f'led_{letra}', 'led', LED_C, 11, linha, Cor=cor))
        fios += [
            fio('placa', pino, 'pcb', f'x1y{linha}', {'v': VERMELHO, 'a': AMARELO, 'g': VERDE}[letra]),
            trilha(f'x1y{linha}', f'x3y{linha}'),       # do pino ao resistor
            trilha(f'x9y{linha}', f'x12y{linha}'),      # do resistor ao anodo
            trilha(f'x11y{linha}', f'x11y{linha + 2}'), # o catodo desce para a trilha de GND
            trilha(f'x11y{linha + 2}', f'x16y{linha + 2}'),
        ]
    # o barramento de GND na coluna 16, por baixo, e o fio do GND do Uno
    fios += [trilha('x16y8', 'x16y14'), trilha('x16y14', 'x16y20'), fio('placa', 'GND.1', 'pcb', 'x16y22', PRETO),
             trilha('x16y20', 'x16y22')]
    codigo = [
        '// Semaforo soldado numa placa perfurada (PCB 18x24).',
        '// Vermelho 2,5 s, verde 2,5 s, amarelo 1 s.',
        'const int VERMELHO = 13, AMARELO = 12, VERDE = 11;',
        '',
        'void acender(int pino, int ms) {',
        '  digitalWrite(VERMELHO, pino == VERMELHO);',
        '  digitalWrite(AMARELO, pino == AMARELO);',
        '  digitalWrite(VERDE, pino == VERDE);',
        '  delay(ms);',
        '}',
        '',
        'void setup() {',
        '  pinMode(VERMELHO, OUTPUT);',
        '  pinMode(AMARELO, OUTPUT);',
        '  pinMode(VERDE, OUTPUT);',
        '}',
        '',
        'void loop() {',
        '  acender(VERMELHO, 2500);',
        '  acender(VERDE, 2500);',
        '  acender(AMARELO, 1000);',
        '}',
    ]
    roteiro = [
        confere('vermelho primeiro na PCB', 3, led={'peca': 'led_v', 'aceso': True}),
        confere('depois o verde na PCB', 5, led={'peca': 'led_g', 'aceso': True}),
        confere('depois o amarelo na PCB', 5, led={'peca': 'led_a', 'aceso': True}),
        confere('sem avisos', 1, sem_avisos=True),
    ]
    projeto('pcb-semaforo', 'Semaforo soldado numa PCB perfurada 18x24 (Uno)', [
        'O semaforo montado como na bancada de solda: os resistores e os LEDs',
        'espetados nos furos da placa perfurada (5 x 7 cm, furos isolados), e as',
        'ligacoes feitas por baixo, de furo a furo, com trilhas de estanho.',
        '',
        'Clique na placa e use "virar (lado de baixo)" na barra: a placa vira com',
        'as pecas, e aparecem as soldas das pernas e as trilhas. Fio feito com a',
        'placa virada e trilha de baixo.',
    ], codigo, pecas, fios, roteiro)


for gerar in [drone, carrinho_rc_sem_placa, carrinho_rc_arduino, rover_4wd, rover_4wd_hc_sr04, rover_4wd_reles,
              fonte_7805_uno, regulador_7805_servos, lm2596_esp32, mt3608_uno, ams1117_lipo_1s,
              semaforo, semaforo_pedestre, botao_esp32, dimmer, servo_pot, sensor_de_re, termometro_lcd,
              alarme_pir, luz_noturna, contador_7seg, relogio_rtc, fechadura, controle_ir, joystick_servos,
              anel_neopixel, oled_grafico, inclinometro, passo_28byj, ventilador, balanca, bluetooth_led,
              espnow, servidor_web, sono_profundo, nrf24, lcd_paralelo, pico_micropython,
              npn_motor_esp32, mosfet_fita_uno, zener_referencia, capacitor_rc_led,
              opto_isolado, fusivel_curto, schottky_protecao, pcb_semaforo]:
    gerar()

# Os que ainda esperam uma correcao do simulador (o resto e regressao).
AINDA_NAO_FUNCIONAM = set()

os.makedirs(SAIDA, exist_ok=True)
roteiros = {}
for nome, p in PROJETOS.items():
    io.open(os.path.join(SAIDA, nome + '.sktch'), 'w', encoding='utf-8', newline='').write(json.dumps({
        'formato': 'sktch',
        'versao': 1,
        'gerado': 'projetos de teste (Alpha 24)',
        'descricao': p['descricao'],
        'codigo': p['codigo'],
        'circuito': {'Pecas': p['pecas'], 'Fios': p['fios']},
    }, ensure_ascii=False, indent=1))
    roteiros[nome] = {'titulo': p['titulo'], 'passos': p['roteiro']}
    # Todo projeto que ja funciona vira teste de regressao da suite.
    if nome not in AINDA_NAO_FUNCIONAM:
        roteiros[nome]['esperado'] = 'funciona'
io.open(os.path.join(SAIDA, 'roteiros.json'), 'w', encoding='utf-8', newline='').write(
    json.dumps(roteiros, ensure_ascii=False, indent=1))
print(f'{len(PROJETOS)} projetos em {SAIDA}')
