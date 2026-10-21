"""Desenha as pecas que nao vem dos wokwi-elements e as poe no catalogo.

Sao quatro, todas pedidas pelo professor:

  junction      no de junca: junta varios fios num ponto so;
  vcc-symbol    simbolo de alimentacao, como em esquema;
  gnd-symbol    simbolo de terra;
  st7789        display colorido de 240x240 (o de 1,3").

O desenho e em milimetros, no tamanho real da peca, e os pinos caem no
passo de 0,1" -- todas espetam na protoboard.

    python ferramentas/extrair-svg/pecas-faceis.py
"""
import io
import json

BASE = 'C:/Users/seraf/OneDrive/Desktop/SimuladorESP32/'
PX = 96 / 25.4                      # mm -> px de 96 dpi
PASSO = 2.54

pecas = []
# O ST7789 foi redesenhado na Alpha 25 (o de 1,54", em pecas-tft.py): rodar este
# script nao o desfaz.
REFEITAS_NA_ALPHA_25 = {'st7789'}


def guardar(id_peca, svg, larg_mm, alt_mm, pinos):
    if id_peca in REFEITAS_NA_ALPHA_25:
        print(f'{id_peca}: refeita na Alpha 25, fica como esta')
        return
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


# ---------------------------------------------------------------
# Junction: quatro pernas curtas num ponto de solda.
# ---------------------------------------------------------------
L = 2 * PASSO / (96 / 25.4) * (96 / 25.4)   # legibilidade: 2 passos
L = 2 * PASSO                                # 5,08 mm
m = L / 2

# A emenda soldada (Alpha 25): as pontas de cobre descascadas ate as quatro
# bordas -- o fio encosta no lado que quiser -- e a gota de estanho no meio,
# a mesma cor das soldas das placas.
junction = f'''<svg xmlns="http://www.w3.org/2000/svg" version="1.1"
     width="{L}mm" height="{L}mm" viewBox="0 0 {L} {L}">
  <defs>
    <radialGradient id="estanho" cx="0.38" cy="0.34" r="0.7"><stop offset="0" stop-color="#f6f8fa"/><stop offset="0.55" stop-color="#c3c9cf"/><stop offset="1" stop-color="#7c838b"/></radialGradient>
  </defs>
  <g stroke-linecap="round">
    <g stroke="#7a4a22" stroke-width="0.62">
      <path d="M{m} 0.35 V{m}"/><path d="M{L - 0.35} {m} H{m}"/><path d="M{m} {L - 0.35} V{m}"/><path d="M0.35 {m} H{m}"/>
    </g>
    <g stroke="#d08a52" stroke-width="0.42">
      <path d="M{m} 0.35 V{m}"/><path d="M{L - 0.35} {m} H{m}"/><path d="M{m} {L - 0.35} V{m}"/><path d="M0.35 {m} H{m}"/>
    </g>
  </g>
  <circle cx="{m}" cy="{m}" r="1.2" fill="url(#estanho)" stroke="#6d747b" stroke-width="0.12"/>
  <circle cx="{m - 0.38}" cy="{m - 0.42}" r="0.32" fill="#ffffff" opacity="0.75"/>
</svg>
'''
guardar('junction', junction, L, L, [
    ('1', m, 0, [], 'ponto de junca'),
    ('2', L, m, [], 'ponto de junca'),
    ('3', m, L, [], 'ponto de junca'),
    ('4', 0, m, [], 'ponto de junca'),
])

# ---------------------------------------------------------------
# VCC: a barra de alimentacao com o pino embaixo.
# ---------------------------------------------------------------
lv, av = 3 * PASSO, 3 * PASSO        # 7,62 x 7,62 mm
meio = lv / 2

# (Alpha 25: o texto em Arial, como o resto das pecas, e a ponta do pino marcada.)
vcc = f'''<svg xmlns="http://www.w3.org/2000/svg" version="1.1"
     width="{lv}mm" height="{av}mm" viewBox="0 0 {lv} {av}"
     font-family="Arial" text-anchor="middle">
  <path d="M1.1 3.3 H{lv - 1.1}" stroke="#e04b4b" stroke-width="0.7" stroke-linecap="round"/>
  <path d="M{meio} 3.3 V{av - 0.5}" stroke="#e04b4b" stroke-width="0.55"/>
  <circle cx="{meio}" cy="{av - 0.5}" r="0.42" fill="#e04b4b"/>
  <text x="{meio}" y="2.25" fill="#e04b4b" font-size="2.2px" font-weight="bold">VCC</text>
</svg>
'''
guardar('vcc-symbol', vcc, lv, av,
        [('VCC', meio, av, ['power'], 'alimentacao positiva')])

# ---------------------------------------------------------------
# GND: as tres barras, com o pino em cima.
# ---------------------------------------------------------------
# (Alpha 25: as barras subiram -- o "GND" encostava na ultima.)
gnd = f'''<svg xmlns="http://www.w3.org/2000/svg" version="1.1"
     width="{lv}mm" height="{av}mm" viewBox="0 0 {lv} {av}"
     font-family="Arial" text-anchor="middle">
  <circle cx="{meio}" cy="0.5" r="0.42" fill="#8a919b"/>
  <path d="M{meio} 0.5 V2.9" stroke="#8a919b" stroke-width="0.55"/>
  <g stroke="#8a919b" stroke-linecap="round">
    <path d="M1.1 3.0 H{lv - 1.1}" stroke-width="0.7"/>
    <path d="M2.1 4.0 H{lv - 2.1}" stroke-width="0.6"/>
    <path d="M3.1 4.9 H{lv - 3.1}" stroke-width="0.5"/>
  </g>
  <text x="{meio}" y="{av - 0.35}" fill="#8a919b" font-size="2.0px" font-weight="bold">GND</text>
</svg>
'''
guardar('gnd-symbol', gnd, lv, av,
        [('GND', meio, 0, ['power'], 'terra')])

# ---------------------------------------------------------------
# ST7789: display colorido de 240x240, o de 1,3".
# ---------------------------------------------------------------
lt, at = 40.0, 38.5
tela_l, tela_a = 27.0, 27.0
tela_x, tela_y = (lt - tela_l) / 2, 8.5

nomes = ['GND', 'VCC', 'SCL', 'SDA', 'RES', 'DC', 'CS', 'BLK']
x0 = (lt - 7 * PASSO) / 2
xs = [x0 + k * PASSO for k in range(8)]
y_pino = 1.7

pads = '\n'.join(
    f'    <rect x="{x - 0.85:.2f}" y="0.55" width="1.7" height="2.4" rx="0.25"/>'
    for x in xs)
rotulos = '\n'.join(
    f'    <tspan x="{x:.2f}" y="4.6">{n}</tspan>' for x, n in zip(xs, nomes))

st7789 = f'''<svg xmlns="http://www.w3.org/2000/svg" version="1.1"
     width="{lt}mm" height="{at}mm" viewBox="0 0 {lt} {at}"
     font-family="monospace" text-anchor="middle">

  <rect x="0" y="0" width="{lt}" height="{at}" rx="1.2" fill="#16406b"/>
  <rect x="0.4" y="0.4" width="{lt - 0.8}" height="{at - 0.8}" rx="1"
        fill="none" stroke="#10304f" stroke-width="0.7"/>

  <!-- conector de 8 vias -->
  <g fill="#e0bb3c" stroke="#a98a1e" stroke-width="0.12">
{pads}
  </g>
  <text fill="#bcd0e4" font-size="1.45px">
{rotulos}
  </text>

  <!-- moldura e vidro: a area util e o que o editor pinta por cima -->
  <rect x="{tela_x - 0.9:.2f}" y="{tela_y - 0.9:.2f}"
        width="{tela_l + 1.8:.2f}" height="{tela_a + 1.8:.2f}" rx="0.5" fill="#0b0d10"/>
  <rect x="{tela_x:.2f}" y="{tela_y:.2f}" width="{tela_l:.2f}" height="{tela_a:.2f}"
        fill="#05070a"/>

  <text x="{lt - 5.5:.2f}" y="{at - 1.2:.2f}" fill="#8fa8c0" font-size="1.5px">240</text>
</svg>
'''
guardar('st7789', st7789, lt, at,
        [(n, x, y_pino, (['power'] if n in ('GND', 'VCC') else []), '')
         for n, x in zip(nomes, xs)])

# ---------------------------------------------------------------
cam = BASE + 'app/assets/pecas.json'
d = json.load(io.open(cam, encoding='utf-8'))
novos = {p['id'] for p in pecas}
d = [p for p in d if p['id'] not in novos] + pecas
d.sort(key=lambda p: p['id'])
io.open(cam, 'w', encoding='utf-8', newline='').write(json.dumps(d, ensure_ascii=False, indent=1))

print(f'\ncatalogo: {len(d)} pecas')
print('area util do ST7789: '
      f'x {tela_x / lt:.4f} y {tela_y / at:.4f} '
      f'larg {tela_l / lt:.4f} alt {tela_a / at:.4f}')
