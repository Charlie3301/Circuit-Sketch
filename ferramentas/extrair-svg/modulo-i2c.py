"""Desenha o modulo de I2C do LCD (PCF8574) e o poe no catalogo.

Esta peca nao vem dos wokwi-elements -- la o I2C e um atributo do
proprio LCD, nao uma peca. O professor pediu o modulo separado, que e
como ele aparece na bancada: uma placinha que se solda atras do display
e troca dezesseis fios por quatro.

O desenho e em milimetros, do tamanho do modulo de verdade (54 x 19 mm),
e os pinos caem no passo de 0,1" -- ele espeta na protoboard.
"""
import io
import json

BASE = 'C:/Users/seraf/OneDrive/Desktop/SimuladorESP32/'
PX = 96 / 25.4                      # mm -> px de 96 dpi
PASSO = 2.54

LARG, ALT = 54.0, 19.0
LCD16 = ['VSS', 'VDD', 'V0', 'RS', 'RW', 'E',
         'D0', 'D1', 'D2', 'D3', 'D4', 'D5', 'D6', 'D7', 'A', 'K']
I2C4 = ['GND', 'VCC', 'SDA', 'SCL']

x_pad = [3.0 + k * PASSO for k in range(16)]
y_pad = 1.9
x_conector = [6.0 + k * PASSO for k in range(4)]
y_conector = 16.6

pads = '\n'.join(
    f'    <rect x="{x - 0.85:.2f}" y="0.7" width="1.7" height="2.6" rx="0.25"/>'
    for x in x_pad)

numeros = '\n'.join(
    f'    <tspan x="{x:.2f}" y="5.1">{k + 1}</tspan>'
    for k, x in enumerate(x_pad) if k % 3 == 0)

conector = '\n'.join(
    f'    <rect x="{x - 0.8:.2f}" y="{y_conector - 1.3:.2f}" width="1.6" height="2.6" rx="0.2"/>'
    for x in x_conector)

nomes = '\n'.join(
    f'    <tspan x="{x:.2f}" y="{y_conector - 2.1:.2f}">{n}</tspan>'
    for x, n in zip(x_conector, I2C4))

# Pernas do chip, dos dois lados do encapsulamento.
pernas = '\n'.join(
    f'    <rect x="{22.4 + k * 1.27:.2f}" y="{y:.2f}" width="0.6" height="1.1"/>'
    for k in range(8) for y in (6.0, 12.4))

svg = f'''<svg xmlns="http://www.w3.org/2000/svg" version="1.1"
     width="{LARG}mm" height="{ALT}mm" viewBox="0 0 {LARG} {ALT}"
     font-family="monospace" text-anchor="middle">

  <!-- placa -->
  <rect x="0" y="0" width="{LARG}" height="{ALT}" rx="1.1" fill="#123a5e"/>
  <rect x="0.35" y="0.35" width="{LARG - 0.7}" height="{ALT - 0.7}" rx="0.9"
        fill="none" stroke="#0d2c47" stroke-width="0.7"/>

  <!-- 16 vias que vao soldadas no LCD -->
  <g fill="#e0bb3c" stroke="#a98a1e" stroke-width="0.12">
{pads}
  </g>
  <text fill="#9fb6cc" font-size="1.5px">
{numeros}
  </text>

  <!-- expansor PCF8574 -->
  <g fill="#8f9aa6">
{pernas}
  </g>
  <rect x="21.6" y="7.0" width="10.5" height="5.4" rx="0.5" fill="#191c20"/>
  <circle cx="23.1" cy="8.4" r="0.55" fill="#33373d"/>
  <text x="26.9" y="10.5" fill="#6f7681" font-size="1.7px">PCF8574</text>

  <!-- trimpot do contraste -->
  <rect x="35.5" y="5.6" width="7.4" height="7.4" rx="0.6" fill="#1f57b5"/>
  <circle cx="39.2" cy="9.3" r="2.5" fill="#dfe3e8"/>
  <rect x="38.9" y="6.9" width="0.6" height="4.8" fill="#8a8f96"/>

  <!-- jumper da luz de fundo -->
  <rect x="45.4" y="6.4" width="5.2" height="2.6" rx="0.3" fill="#12161b"/>
  <g fill="#e0bb3c">
    <rect x="46.2" y="7.0" width="1.3" height="1.4"/>
    <rect x="48.5" y="7.0" width="1.3" height="1.4"/>
  </g>
  <text x="48.0" y="11.6" fill="#9fb6cc" font-size="1.5px">LED</text>

  <!-- conector de 4 vias: e a razao da peca existir -->
  <rect x="{x_conector[0] - 2.0:.2f}" y="{y_conector - 1.9:.2f}"
        width="{x_conector[-1] - x_conector[0] + 4.0:.2f}" height="3.8"
        rx="0.4" fill="#11161c"/>
  <g fill="#e0bb3c" stroke="#a98a1e" stroke-width="0.12">
{conector}
  </g>
  <text fill="#cfd3d8" font-size="1.4px">
{nomes}
  </text>
</svg>
'''

io.open(BASE + 'app/assets/modulo-i2c.svg', 'w', encoding='utf-8', newline='').write(svg)

pinos = [{'nome': n, 'x': round(x * PX, 2), 'y': round(y_pad * PX, 2),
          'sinais': (['power'] if n in ('VSS', 'VDD') else []), 'descricao': ''}
         for x, n in zip(x_pad, LCD16)]
pinos += [{'nome': n, 'x': round(x * PX, 2), 'y': round(y_conector * PX, 2),
           'sinais': (['power'] if n in ('GND', 'VCC') else []), 'descricao': ''}
          for x, n in zip(x_conector, I2C4)]

cam = BASE + 'app/assets/pecas.json'
d = json.load(io.open(cam, encoding='utf-8'))
d = [p for p in d if p['id'] != 'modulo-i2c']
d.append({'id': 'modulo-i2c', 'largura': round(LARG * PX), 'altura': round(ALT * PX),
          'pinos': pinos, 'variantes': [], 'metades': False})
d.sort(key=lambda p: p['id'])
io.open(cam, 'w', encoding='utf-8', newline='').write(json.dumps(d, ensure_ascii=False, indent=1))

print('modulo-i2c:', round(LARG * PX), 'x', round(ALT * PX), 'px,', len(pinos), 'pinos')
