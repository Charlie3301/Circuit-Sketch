"""python ferramentas/extrair-svg/demos-alpha18.py

Uma demo para cada placa nova da Alpha 18.

Toda placa do programa tem que abrir com alguma coisa que FUNCIONA --
e uma regra antiga daqui, conferida pelo autoteste. Com vinte e seis
placas novas, escrever as vinte e seis a mao daria vinte e seis chances
de errar o nome de um pino.

Entao a demo e sempre a mesma: um LED piscando, ligado por fio num pino
da placa, com o resistor no meio. E o "olá mundo" de microcontrolador, e
ele PROVA as duas coisas que mudam de placa para placa -- que o nome do
pino da serigrafia virou o GPIO certo, e que o fio chega la.

O codigo sai em C++ ou em MicroPython conforme a placa, porque e assim
que cada uma e programada na pratica.
"""

# Aposentado na Alpha 19: as demos de pisca-pisca sairam do programa, e cada
# placa (e linguagem) ganhou um projeto proprio em demos-por-placa.py. O codigo
# fica aqui como historico; rodar este arquivo recriaria demos que nenhum
# menu lista.
raise SystemExit('aposentado: use ferramentas/extrair-svg/demos-por-placa.py')

import io
import json

BASE = 'C:/Users/seraf/OneDrive/Desktop/SimuladorESP32/'
PASTA = BASE + 'app/assets/demos/'


def cpp(comentario, pino, rotulo):
    return [
        f'// {comentario}',
        '// Um LED ligado no pino, com resistor de 330 ohm.',
        '',
        f'const int LED = {pino};   // {rotulo}',
        '',
        'void setup() {',
        '  pinMode(LED, OUTPUT);',
        '}',
        '',
        'void loop() {',
        '  digitalWrite(LED, HIGH);',
        '  delay(500);',
        '  digitalWrite(LED, LOW);',
        '  delay(500);',
        '}',
    ]


def micropython(comentario, pino, rotulo):
    return [
        f'# {comentario}',
        '# Um LED ligado no pino, com resistor de 330 ohm. Em MicroPython.',
        '',
        'from machine import Pin',
        'import time',
        '',
        f'led = Pin({pino}, Pin.OUT)   # {rotulo}',
        '',
        'while True:',
        '    led.value(1)',
        '    time.sleep(0.5)',
        '    led.value(0)',
        '    time.sleep(0.5)',
    ]


# O pino em que o LED externo entra em cada placa, pelo NOME da
# serigrafia. Nao ha um nome que sirva para todas: as DevKit novas usam
# o numero puro, a NodeMCU renumerou tudo, e os modulos ESP-05 e ESP-10
# so trazem para fora os pinos da serial -- neles o LED vai mesmo no TX,
# que e o GPIO1 e e o unico jeito de piscar alguma coisa.
PINO_EXTERNO = {
    'esp32-s2-devkit': '4', 'esp32-s3-devkit': '4', 'esp32-c3-devkit': '4',
    'esp32-c6-devkit': '4', 'esp32-h2-devkit': '4',
    'esp8266-nodemcu': 'D5',
    'esp-01': 'GPIO2', 'esp-02': 'GPIO2', 'esp-11': 'GPIO2',

    # O ESP-09 nao traz o GPIO2 para fora: nele o LED vai no GPIO4.
    'esp-09': 'GPIO4',

    # Nos de cinco pinos nao ha GPIO nenhum a nao ser os da serial.
    # Piscar no TXD e feio e e o unico jeito.
    'esp-05': 'TXD', 'esp-10': 'TXD',
    'attiny85': 'PB0',
    'st-nucleo-l031k6': 'D5', 'st-nucleo-c031c6': 'D5',
    'pi-pico-w': 'GP15',
}


def pino_externo(tipo):
    return PINO_EXTERNO.get(tipo, 'GPIO4')


# id do recurso, nome no menu, placa, tipo da peca, linguagem
DEMOS = [
    # ---- as variantes de ESP32 ----
    ('esp32s2-pisca', 'Pisca-pisca', 'ESP32-S2 DevKit', 'esp32-s2-devkit', cpp),
    ('esp32s3-pisca', 'Pisca-pisca', 'ESP32-S3 DevKit', 'esp32-s3-devkit', cpp),
    ('esp32c3-pisca', 'Pisca-pisca', 'ESP32-C3 DevKit', 'esp32-c3-devkit', cpp),
    ('esp32c6-pisca', 'Pisca-pisca', 'ESP32-C6 DevKit', 'esp32-c6-devkit', cpp),
    ('esp32h2-pisca', 'Pisca-pisca', 'ESP32-H2 DevKit', 'esp32-h2-devkit', cpp),

    # ---- ESP8266 ----
    ('nodemcu-pisca', 'Pisca-pisca', 'NodeMCU (ESP8266)', 'esp8266-nodemcu', cpp),
]

MODULOS = [
    ('esp-01', 'ESP-01'), ('esp-02', 'ESP-02'), ('esp-03', 'ESP-03'),
    ('esp-04', 'ESP-04'), ('esp-05', 'ESP-05'), ('esp-06', 'ESP-06'),
    ('esp-07', 'ESP-07'), ('esp-08', 'ESP-08'), ('esp-09', 'ESP-09'),
    ('esp-10', 'ESP-10'), ('esp-11', 'ESP-11'), ('esp-12', 'ESP-12'),
    ('esp-12e', 'ESP-12E'), ('esp-12f', 'ESP-12F'),
    ('esp-13', 'ESP-13'), ('esp-14', 'ESP-14'),
]

for tipo, nome in MODULOS:
    DEMOS.append((f'{tipo}-pisca', 'Pisca-pisca', nome, tipo, cpp))

DEMOS += [
    # ---- o chip solto ----
    ('attiny85-pisca', 'Pisca-pisca', 'ATtiny85', 'attiny85', cpp),

    # ---- as Nucleo, brancas ----
    ('nucleo-l031-pisca', 'Pisca-pisca', 'ST Nucleo-L031K6', 'st-nucleo-l031k6', cpp),
    ('nucleo-c031-pisca', 'Pisca-pisca', 'ST Nucleo-C031C6', 'st-nucleo-c031c6', cpp),

    # ---- a Pico W, que se programa em MicroPython ----
    ('picow-pisca', 'Pisca-pisca', 'Raspberry Pi Pico W', 'pi-pico-w', micropython),
]

def externo_numero(tipo, nome):
    """O numero que o sketch escreve para este pino."""
    if tipo == 'esp8266-nodemcu':
        return {'D5': 14}[nome]
    if tipo == 'attiny85':
        return int(nome[2:])
    if tipo.startswith('st-nucleo'):
        return int(nome[1:])
    if tipo == 'pi-pico-w':
        return int(nome[2:])
    if nome in ('TX', 'TXD'):
        return 1
    if nome.startswith('GPIO'):
        return int(nome[4:])
    return int(nome)


escritas = []

for recurso, nome, placa, tipo, feitio in DEMOS:
    # O LED vai de FORA, ligado por fio.
    #
    # So o LED de bordo nao bastaria: metade das placas novas nao tem
    # um, e um circuito com uma placa sozinha nao ensina nada sobre
    # ligar. Com o LED e o resistor a demo mostra as duas coisas -- o
    # pino certo e o resistor que impede o LED de queimar.
    externo = pino_externo(tipo)

    projeto = {
        'formato': 'sktch',
        'versao': 1,
        'gerado': 'demo do CircuitSketch',
        'codigo': feitio(f'Pisca-pisca na {placa}.', externo_numero(tipo, externo), externo),
        'circuito': {
            'Pecas': [
                {'Id': 'placa', 'Tipo': tipo, 'X': 120, 'Y': 60},
                {'Id': 'led', 'Tipo': 'led', 'X': 420, 'Y': 120, 'Cor': 'vermelho'},
                {'Id': 'r', 'Tipo': 'resistor', 'X': 420, 'Y': 240, 'Ohms': 330},
            ],
            'Fios': [
                {'DePeca': 'placa', 'DePino': externo,
                 'ParaPeca': 'led', 'ParaPino': 'A', 'Cor': '#00A651'},
                {'DePeca': 'led', 'DePino': 'C',
                 'ParaPeca': 'r', 'ParaPino': '1', 'Cor': '#1A1A1A'},
                {'DePeca': 'r', 'DePino': '2',
                 'ParaPeca': 'placa', 'ParaPino': 'GND', 'Cor': '#1A1A1A'},
            ],
        },
    }

    io.open(PASTA + recurso + '.sktch', 'w', encoding='utf-8', newline='').write(
        json.dumps(projeto, ensure_ascii=False, indent=1))

    escritas.append((recurso, nome, placa))
    print(f'{recurso}.sktch  ({placa})')

print(f'\n{len(escritas)} demos novas')

# A lista para colar no Demos.Todas.
print('\n--- para o Recentes.cs ---')
for recurso, nome, placa in escritas:
    print(f'        new("{recurso}", {chr(34)}{nome}{chr(34)}, "{placa}"),')
