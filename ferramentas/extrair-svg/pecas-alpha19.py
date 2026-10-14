"""python ferramentas/extrair-svg/pecas-alpha19.py

As pecas de som da Alpha 19 (pedido do DOOM):

  - alto-falante : alto-falante de 8 ohms (28 mm, 0,5 W). Ligado num
                   amplificador, toca o som que a placa manda pelo I2S; direto
                   num pino, so aguenta com resistor (o GPIO da 40 mA, e 3,3 V
                   em 8 ohms pedem 400).
  - max98357a    : o amplificador I2S classe D da placa da Adafruit (e dos
                   modulos roxos iguais): LRC, BCLK, DIN, GAIN, SD, GND, VIN
                   embaixo, e o borne do alto-falante em cima.

O desenho e simples de proposito: a arte definitiva vem depois.
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
#  Alto-falante de 8 ohms, 28 mm
# =====================================================================
lfal, afal = 28.0, 32.6
cx, cy = lfal / 2, 14.0
xmais, xmenos = cx - PASSO / 2, cx + PASSO / 2

aneis = '\n'.join(
    f'  <circle cx="{cx}" cy="{cy}" r="{r}" fill="none" stroke="#2e3138" stroke-width="0.45"/>'
    for r in (9.8, 8.0, 6.2))

alto_falante = f'''<svg xmlns="http://www.w3.org/2000/svg" version="1.1"
     width="{lfal}mm" height="{afal}mm" viewBox="0 0 {lfal} {afal}">

  <!-- fios: o vermelho e o + -->
  <path d="M{xmais:.2f} 25.0 V{afal - 0.4:.2f}" stroke="#d62a2a" stroke-width="0.6" stroke-linecap="round"/>
  <path d="M{xmenos:.2f} 25.0 V{afal - 0.4:.2f}" stroke="#5a5f68" stroke-width="0.6" stroke-linecap="round"/>

  <!-- aro de metal -->
  <circle cx="{cx}" cy="{cy}" r="13.6" fill="#2b2e35" stroke="#15171b" stroke-width="0.4"/>
  <circle cx="{cx}" cy="{cy}" r="12.5" fill="#434852"/>

  <!-- cone -->
  <circle cx="{cx}" cy="{cy}" r="11.3" fill="#1c1e23"/>
{aneis}

  <!-- cupula -->
  <circle cx="{cx}" cy="{cy}" r="3.8" fill="#50555f"/>
  <circle cx="{cx - 0.9}" cy="{cy - 0.9}" r="1.5" fill="#666c77"/>

  <text x="{cx}" y="{cy + 7.9:.2f}" fill="#8b9199" font-size="1.6px"
        text-anchor="middle" font-family="Verdana">8&#937; 0,5W</text>

  <text x="{xmais - 1.5:.2f}" y="{afal - 2.0:.2f}" fill="#d05a5a" font-size="2.1px"
        text-anchor="middle" font-family="Verdana">+</text>
  <text x="{xmenos + 1.5:.2f}" y="{afal - 2.0:.2f}" fill="#8b9199" font-size="2.4px"
        text-anchor="middle" font-family="Verdana">-</text>
</svg>
'''

guardar('alto-falante', alto_falante, lfal, afal, [
    ('+', xmais, afal - 0.4, [], 'Positivo (fio vermelho)'),
    ('-', xmenos, afal - 0.4, [], 'Negativo'),
])


# =====================================================================
#  Amplificador I2S MAX98357A
# =====================================================================
lamp, aamp = 20.32, 19.4
nomes_amp = ['LRC', 'BCLK', 'DIN', 'GAIN', 'SD', 'GND', 'VIN']
x0_amp = lamp / 2 - 3 * PASSO
xs_amp = [x0_amp + i * PASSO for i in range(7)]
y_amp = aamp - 1.6
xb_mais, xb_menos = lamp / 2 - 1.75, lamp / 2 + 1.75   # borne de 3,5 mm

furos_amp = '\n'.join(
    f'  <circle cx="{x:.2f}" cy="{y_amp:.2f}" r="0.62" fill="#101418"/>' for x in xs_amp)
pads_amp = '\n'.join(
    f'  <rect x="{x - 0.95:.2f}" y="{y_amp - 0.95:.2f}" width="1.9" height="1.9" rx="0.35" fill="#d8b24a"/>'
    for x in xs_amp)
rotulos_amp = '\n'.join(
    f'  <text x="{x + 0.38:.2f}" y="{y_amp - 1.5:.2f}" transform="rotate(-90 {x + 0.38:.2f} {y_amp - 1.5:.2f})" '
    f'fill="#e6ecf5" font-size="1.05px" text-anchor="start" font-family="Verdana">{n}</text>'
    for x, n in zip(xs_amp, nomes_amp))

max98357a = f'''<svg xmlns="http://www.w3.org/2000/svg" version="1.1"
     width="{lamp}mm" height="{aamp}mm" viewBox="0 0 {lamp} {aamp}">

  <!-- placa -->
  <rect x="0.2" y="2.6" width="{lamp - 0.4:.2f}" height="{aamp - 2.8:.2f}" rx="1.2" fill="#233a73"/>
  <rect x="0.2" y="2.6" width="{lamp - 0.4:.2f}" height="{aamp - 2.8:.2f}" rx="1.2"
        fill="none" stroke="#162552" stroke-width="0.3"/>

  <!-- borne do alto-falante -->
  <rect x="{lamp / 2 - 3.6:.2f}" y="0.3" width="7.2" height="6.4" rx="0.5" fill="#2f8f5a"/>
  <rect x="{lamp / 2 - 3.6:.2f}" y="0.3" width="7.2" height="1.6" rx="0.5" fill="#37a668"/>
  <circle cx="{xb_mais:.2f}" cy="4.2" r="1.2" fill="#c9ced6"/>
  <circle cx="{xb_menos:.2f}" cy="4.2" r="1.2" fill="#c9ced6"/>
  <path d="M{xb_mais - 0.8:.2f} 4.2 H{xb_mais + 0.8:.2f} M{xb_menos - 0.8:.2f} 4.2 H{xb_menos + 0.8:.2f}"
        stroke="#6d737c" stroke-width="0.3"/>
  <text x="{xb_mais - 2.9:.2f}" y="6.2" fill="#e6ecf5" font-size="1.4px" text-anchor="middle" font-family="Verdana">+</text>
  <text x="{xb_menos + 2.9:.2f}" y="6.2" fill="#e6ecf5" font-size="1.6px" text-anchor="middle" font-family="Verdana">-</text>

  <!-- o chip -->
  <rect x="{lamp / 2 - 1.6:.2f}" y="7.4" width="3.2" height="2.6" rx="0.3" fill="#15171b"/>
  <circle cx="{lamp / 2 - 1.1:.2f}" cy="7.85" r="0.16" fill="#4c5058"/>
  <text x="{lamp / 2:.2f}" y="11.3" fill="#e6ecf5" font-size="1.15px"
        text-anchor="middle" font-family="Verdana">MAX98357A</text>

{pads_amp}
{furos_amp}
{rotulos_amp}
</svg>
'''

descricoes_amp = {
    'LRC': 'Relogio das palavras do I2S (WS, LRCLK)',
    'BCLK': 'Relogio dos bits do I2S',
    'DIN': 'Dados do I2S: o som',
    'GAIN': 'Ganho: solto = 9 dB, no GND = 12 dB, no VIN = 6 dB',
    'SD': 'No GND desliga o amplificador; solto, toca a media dos dois canais',
    'GND': 'Terra',
    'VIN': 'Alimentacao, de 2,5 a 5,5 V',
}

guardar('max98357a', max98357a, lamp, aamp, [
    ('OUT+', xb_mais, 0.3, [], 'Alto-falante, positivo (4 a 8 ohms)'),
    ('OUT-', xb_menos, 0.3, [], 'Alto-falante, negativo'),
] + [
    (n, x, y_amp, (['power'] if n in ('GND', 'VIN') else []), descricoes_amp[n])
    for n, x in zip(nomes_amp, xs_amp)
])


cam = BASE + 'app/assets/pecas.json'
d = json.load(io.open(cam, encoding='utf-8'))
novos = {p['id'] for p in pecas}
d = [p for p in d if p['id'] not in novos] + pecas
d.sort(key=lambda p: p['id'])
io.open(cam, 'w', encoding='utf-8', newline='').write(json.dumps(d, ensure_ascii=False, indent=1))

print(f'\ncatalogo: {len(d)} pecas')
