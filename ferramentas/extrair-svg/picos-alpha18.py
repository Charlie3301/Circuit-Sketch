"""python ferramentas/extrair-svg/picos-alpha18.py

As seis entradas de Raspberry Pi Pico que a Alpha 18 pediu:

    Pi Pico                     Arduino (C++)
    Pi Pico MicroPython         MicroPython
    Pi Pico SDK (Avancado)      C, o SDK da Raspberry
    Pi Pico W                   Arduino (C++)
    Pi Pico W MicroPython       MicroPython
    Pi Pico W SDK (Avancado)    C, o SDK da Raspberry

Sao DUAS placas e TRES jeitos de programar cada uma, e nao seis placas:
a Pico e a Pico W sao as unicas pecas de verdade. Criar seis pecas para
duas placas encheria a paleta de copias e faria o aluno escolher entre
coisas identicas.

Entao cada combinacao vira um PROJETO de partida, que e onde a escolha
importa de fato -- o que muda entre elas e o codigo, e e o codigo que
vem pronto aqui.
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


def arduino(placa, pino):
    return [
        f'// Pisca-pisca na {placa}, com o Arduino.',
        '// Um LED ligado no pino, com resistor de 330 ohm.',
        '',
        f'const int LED = {pino};',
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


def micropython(placa, pino):
    return [
        f'# Pisca-pisca na {placa}, em MicroPython.',
        '# Um LED ligado no pino, com resistor de 330 ohm.',
        '',
        'from machine import Pin',
        'import time',
        '',
        f'led = Pin({pino}, Pin.OUT)',
        '',
        'while True:',
        '    led.value(1)',
        '    time.sleep(0.5)',
        '    led.value(0)',
        '    time.sleep(0.5)',
    ]


def sdk(placa, pino):
    return [
        f'// Pisca-pisca na {placa}, com o SDK em C da Raspberry.',
        '//',
        '// Aqui nao ha setup nem loop: o programa comeca no main(), e o',
        '// laco infinito e escrito na mao. E assim que se programa a',
        '// Pico sem o Arduino no meio.',
        '',
        '#include "pico/stdlib.h"',
        '',
        f'#define LED {pino}',
        '',
        'int main() {',
        '    stdio_init_all();',
        '',
        '    gpio_init(LED);',
        '    gpio_set_dir(LED, GPIO_OUT);',
        '',
        '    while (true) {',
        '        gpio_put(LED, 1);',
        '        sleep_ms(500);',
        '        gpio_put(LED, 0);',
        '        sleep_ms(500);',
        '    }',
        '}',
    ]


PICOS = [
    ('pico-arduino', 'Pi Pico', 'Raspberry Pi Pico', 'pi-pico', 'GP15', 15, arduino),
    ('pico-upython', 'Pi Pico MicroPython', 'Raspberry Pi Pico', 'pi-pico',
     'GP15', 15, micropython),
    ('pico-sdk', 'Pi Pico SDK (Avançado)', 'Raspberry Pi Pico', 'pi-pico',
     'GP15', 15, sdk),

    ('picow-arduino', 'Pi Pico W', 'Raspberry Pi Pico W', 'pi-pico-w', 'GP15', 15, arduino),
    ('picow-upython', 'Pi Pico W MicroPython', 'Raspberry Pi Pico W', 'pi-pico-w',
     'GP15', 15, micropython),
    ('picow-sdk', 'Pi Pico W SDK (Avançado)', 'Raspberry Pi Pico W', 'pi-pico-w',
     'GP15', 15, sdk),
]

for recurso, nome, placa, tipo, pino, numero, feitio in PICOS:
    projeto = {
        'formato': 'sktch',
        'versao': 1,
        'gerado': 'demo do CircuitSketch',
        'codigo': feitio(placa, numero),
        'circuito': {
            'Pecas': [
                {'Id': 'placa', 'Tipo': tipo, 'X': 120, 'Y': 60},
                {'Id': 'led', 'Tipo': 'led', 'X': 420, 'Y': 120, 'Cor': 'vermelho'},
                {'Id': 'r', 'Tipo': 'resistor', 'X': 420, 'Y': 240, 'Ohms': 330},
            ],
            'Fios': [
                {'DePeca': 'placa', 'DePino': pino,
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

    print(f'{recurso}.sktch  ({nome})')

print('\n--- para o Recentes.cs ---')
for recurso, nome, placa, *_ in PICOS:
    print(f'        new("{recurso}", "{nome}", "{placa}"),')
