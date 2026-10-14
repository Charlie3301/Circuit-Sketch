"""python ferramentas/extrair-svg/pecas-alpha17.py

As pecas que entraram na Alpha 17, da lista do professor:

  - ssd1306-i2c : o display OLED de QUATRO pinos, que e o que a turma
                  compra. O que existia era a versao SPI, de oito -- e a
                  pergunta dele foi exatamente "por que oito?".
  - bateria     : pilha com capacidade, tensao e corrente configuraveis.
"""
import io
import json

BASE = 'C:/Users/seraf/OneDrive/Desktop/SimuladorESP32/'
PX = 96 / 25.4
PASSO = 2.54

pecas = []


def guardar(id_peca, svg, larg_mm, alt_mm, pinos):
    io.open(BASE + f'app/assets/{id_peca}.svg', 'w', encoding='utf-8', newline='').write(svg)
    pecas.append({
        'id': id_peca,
        'largura': round(larg_mm * PX),
        'altura': round(alt_mm * PX),
        'pinos': [{'nome': n, 'x': round(x * PX, 2), 'y': round(y * PX, 2),
                   'sinais': s, 'descricao': d}
                  for n, x, y, s, d in pinos],
        'variantes': [], 'metades': False,
    })
    print(f'{id_peca}: {round(larg_mm * PX)}x{round(alt_mm * PX)} px, {len(pinos)} pinos')


# =====================================================================
#  Display OLED I2C 0,96" -- o modulo de 4 pinos
# =====================================================================
#
# Medidas do modulo de verdade: 27,3 x 27,8 mm, com a area de vidro
# preta ocupando quase toda a metade de baixo e a barra de pinos em
# cima, no passo de 2,54.
loled, aoled = 27.3, 27.8

nomes_oled = ['GND', 'VCC', 'SCL', 'SDA']
x0_oled = loled / 2 - 1.5 * PASSO
xs_oled = [x0_oled + i * PASSO for i in range(4)]

furos_oled = '\n'.join(
    f'    <circle cx="{x:.2f}" cy="1.9" r="0.62" fill="#12161c"/>' for x in xs_oled)

pads_oled = '\n'.join(
    f'    <rect x="{x - 1.0:.2f}" y="0.55" width="2.0" height="2.7" rx="0.45" '
    f'fill="#d8b24a"/>' for x in xs_oled)

rotulos_oled = '\n'.join(
    f'    <text x="{x:.2f}" y="5.1" fill="#8fa3bd" font-size="1.55px" '
    f'text-anchor="middle" font-family="Verdana">{n}</text>'
    for x, n in zip(xs_oled, nomes_oled))

oled = f'''<svg xmlns="http://www.w3.org/2000/svg" version="1.1"
     width="{loled}mm" height="{aoled}mm" viewBox="0 0 {loled} {aoled}">

  <!-- placa -->
  <rect x="0" y="0" width="{loled}" height="{aoled}" rx="0.9" fill="#0e4a8a"/>
  <rect x="0.15" y="0.15" width="{loled - 0.3}" height="{aoled - 0.3}" rx="0.8"
        fill="none" stroke="#0a3868" stroke-width="0.2"/>
  <rect x="0" y="0" width="{loled}" height="{aoled}" rx="0.9"
        fill="none" stroke="#0a3868" stroke-width="0.3"/>

  <!-- furos de fixacao -->
  <g fill="#0a3868">
    <circle cx="1.5" cy="{aoled - 1.5:.2f}" r="0.85"/>
    <circle cx="{loled - 1.5:.2f}" cy="{aoled - 1.5:.2f}" r="0.85"/>
  </g>

  <!-- barra de pinos -->
  <g>
{pads_oled}
  </g>
  <g>
{furos_oled}
  </g>
{rotulos_oled}

  <!-- vidro do display: a area preta e maior que a acesa, como no modulo -->
  <rect x="1.4" y="6.4" width="{loled - 2.8}" height="17.4" rx="0.5" fill="#0a0d12"/>
  <rect x="1.4" y="6.4" width="{loled - 2.8}" height="17.4" rx="0.5"
        fill="none" stroke="#050709" stroke-width="0.25"/>

  <!-- a area que realmente acende: 128x64 em 21,7 x 10,9 mm -->
  <rect x="2.8" y="9.7" width="21.7" height="10.9" fill="#05070a"/>

  <!-- brilho de vidro, bem discreto -->
  <path d="M1.9 7.0 h{loled - 3.8:.2f} v1.5 h-{loled - 3.8:.2f} z"
        fill="#ffffff" opacity="0.05"/>

  <text x="{loled / 2:.2f}" y="26.6" fill="#7e93ad" font-size="1.5px"
        text-anchor="middle" font-family="Verdana">OLED 128x64 I2C</text>
</svg>
'''

guardar('ssd1306-i2c', oled, loled, aoled, [
    (n, x, 1.9, (['power'] if n in ('VCC', 'GND') else ['i2c']),
     {'GND': 'Terra', 'VCC': 'Alimentacao (3,3 ou 5 V)',
      'SCL': 'Relogio do I2C', 'SDA': 'Dados do I2C'}[n])
    for n, x in zip(nomes_oled, xs_oled)
])


# =====================================================================
#  Bateria -- pilha com capacidade, tensao e corrente configuraveis
# =====================================================================
lbat, abat = 34.0, 19.0

bateria = f'''<svg xmlns="http://www.w3.org/2000/svg" version="1.1"
     width="{lbat}mm" height="{abat}mm" viewBox="0 0 {lbat} {abat}">

  <!-- corpo -->
  <rect x="0.4" y="3.2" width="{lbat - 0.8}" height="12.0" rx="1.4" fill="#2b2f36"/>
  <rect x="0.4" y="3.2" width="{lbat - 0.8}" height="12.0" rx="1.4"
        fill="none" stroke="#1a1d22" stroke-width="0.35"/>

  <!-- rotulo -->
  <rect x="1.6" y="4.4" width="{lbat - 3.2}" height="9.6" rx="0.8" fill="#1f6f4a"/>
  <rect x="1.6" y="4.4" width="{lbat - 3.2}" height="3.1" rx="0.8" fill="#258457"/>

  <!-- o simbolo de pilha: tracos longo e curto -->
  <g stroke="#e9f3ec" stroke-width="0.55" stroke-linecap="round">
    <path d="M9.0 7.4 V11.0"/>
    <path d="M11.4 8.3 V10.1"/>
    <path d="M13.8 7.4 V11.0"/>
    <path d="M16.2 8.3 V10.1"/>
  </g>

  <text x="{lbat - 4.0:.2f}" y="10.6" fill="#cfe6d8" font-size="2.0px"
        text-anchor="end" font-family="Verdana">BATERIA</text>

  <!-- terminais -->
  <g stroke="#9aa0a6" stroke-width="0.5" stroke-linecap="round" fill="none">
    <path d="M{lbat / 2 - PASSO / 2:.2f} 15.0 V{abat - 0.4:.2f}"/>
    <path d="M{lbat / 2 + PASSO / 2:.2f} 15.0 V{abat - 0.4:.2f}"/>
  </g>

  <text x="{lbat / 2 - PASSO / 2:.2f}" y="2.6" fill="#d05a5a" font-size="2.2px"
        text-anchor="middle" font-family="Verdana">+</text>
  <text x="{lbat / 2 + PASSO / 2:.2f}" y="2.6" fill="#8b9199" font-size="2.6px"
        text-anchor="middle" font-family="Verdana">-</text>
</svg>
'''

guardar('bateria', bateria, lbat, abat, [
    ('+', lbat / 2 - PASSO / 2, abat - 0.4, ['power'], 'Positivo'),
    ('-', lbat / 2 + PASSO / 2, abat - 0.4, ['power'], 'Negativo (terra)'),
])


cam = BASE + 'app/assets/pecas.json'
d = json.load(io.open(cam, encoding='utf-8'))
novos = {p['id'] for p in pecas}
d = [p for p in d if p['id'] not in novos] + pecas
d.sort(key=lambda p: p['id'])
io.open(cam, 'w', encoding='utf-8', newline='').write(json.dumps(d, ensure_ascii=False, indent=1))

print(f'\ncatalogo: {len(d)} pecas')
