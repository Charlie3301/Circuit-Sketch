"""python ferramentas/extrair-svg/pecas-rede.py"""
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


lap, aap = 34.0, 24.0

wifi_ap = f'''<svg xmlns="http://www.w3.org/2000/svg" version="1.1"
     width="{lap}mm" height="{aap}mm" viewBox="0 0 {lap} {aap}"
     font-family="monospace" text-anchor="middle">

  <g stroke="#2b3138" stroke-width="0.9" stroke-linecap="round">
    <path d="M7.5 11 L4.2 1.6"/>
    <path d="M26.5 11 L29.8 1.6"/>
  </g>
  <circle cx="4.2" cy="1.6" r="0.95" fill="#3a424b"/>
  <circle cx="29.8" cy="1.6" r="0.95" fill="#3a424b"/>

  <rect x="2" y="9.5" width="30" height="12.5" rx="2.2" fill="#22272d"/>
  <rect x="2" y="9.5" width="30" height="12.5" rx="2.2"
        fill="none" stroke="#151a1f" stroke-width="0.45"/>
  <rect x="2.9" y="10.3" width="28.2" height="4.4" rx="1.4" fill="#2c333a"/>

  <g fill="none" stroke="#4fc3f7" stroke-width="0.62" stroke-linecap="round">
    <path d="M14.1 13.4 a2.7 2.7 0 0 1 3.8 0"/>
    <path d="M12.3 11.5 a5.3 5.3 0 0 1 7.4 0"/>
    <path d="M10.5 9.6 a7.9 7.9 0 0 1 11 0"/>
  </g>
  <circle cx="16" cy="14.6" r="0.75" fill="#4fc3f7"/>

  <g>
    <circle cx="7.4" cy="19.2" r="0.72" fill="#43d17a"/>
    <circle cx="10.2" cy="19.2" r="0.72" fill="#43d17a"/>
    <circle cx="13" cy="19.2" r="0.72" fill="#f0a63c"/>
  </g>
  <text x="23.5" y="19.9" fill="#7c8894" font-size="2.1px">AP</text>

  <g fill="#1b2026">
    <rect x="4.2" y="21.6" width="3.4" height="1.6" rx="0.5"/>
    <rect x="26.4" y="21.6" width="3.4" height="1.6" rx="0.5"/>
  </g>
</svg>
'''
# o wifi-ap foi refeito na Alpha 25 (pecas-wifi-ap.py): aqui fica de fora
# guardar('wifi-ap', wifi_ap, lap, aap, [])


lnrf, anrf = 17.0, 29.0
colunas = [2.4, 2.4 + PASSO]
linhas = [3.2 + i * PASSO for i in range(4)]
nomes = [('GND', 'VCC'), ('CE', 'CSN'), ('SCK', 'MOSI'), ('MISO', 'IRQ')]

pads = '\n'.join(
    f'    <circle cx="{x:.2f}" cy="{y:.2f}" r="0.82"/>'
    for y in linhas for x in colunas)

furos = '\n'.join(
    f'    <circle cx="{x:.2f}" cy="{y:.2f}" r="0.38"/>'
    for y in linhas for x in colunas)

rotulos = '\n'.join(
    f'    <tspan x="{colunas[1] + 1.5:.2f}" y="{y + 0.38:.2f}">{a}/{b}</tspan>'
    for y, (a, b) in zip(linhas, nomes))

meandro = ('M11.0 26.6 h3.4 v-1.3 h-3.4 v-1.3 h3.4 v-1.3 h-3.4 v-1.3 h3.4 v-1.3 h-3.4 '
           'v-1.3 h3.4')

nrf = f'''<svg xmlns="http://www.w3.org/2000/svg" version="1.1"
     width="{lnrf}mm" height="{anrf}mm" viewBox="0 0 {lnrf} {anrf}"
     font-family="monospace" text-anchor="start">

  <rect x="0" y="0" width="{lnrf}" height="{anrf}" rx="0.8" fill="#1d4f8c"/>
  <rect x="0.35" y="0.35" width="{lnrf - 0.7}" height="{anrf - 0.7}" rx="0.6"
        fill="none" stroke="#153b69" stroke-width="0.3"/>

  <g fill="#e0bb3c" stroke="#a98a1e" stroke-width="0.1">
{pads}
  </g>
  <g fill="#123a66">
{furos}
  </g>
  <text fill="#cfe0f2" font-size="0.95px">
{rotulos}
  </text>

  <rect x="3.0" y="14.0" width="7.6" height="7.6" rx="0.5" fill="#8e99a6"/>
  <rect x="3.0" y="14.0" width="7.6" height="7.6" rx="0.5"
        fill="none" stroke="#5f6a76" stroke-width="0.25"/>
  <circle cx="4.0" cy="15.0" r="0.35" fill="#5f6a76"/>
  <text x="3.7" y="18.4" fill="#3d4650" font-size="1.5px">nRF24</text>

  <rect x="12.0" y="14.4" width="2.4" height="1.5" rx="0.25" fill="#c8ccd2"/>
  <rect x="12.0" y="17.0" width="2.4" height="1.5" rx="0.25" fill="#c8ccd2"/>
  <circle cx="13.2" cy="20.4" r="0.85" fill="#2b2f36"/>

  <path d="{meandro}" fill="none" stroke="#d7b94a" stroke-width="0.55"
        stroke-linecap="square"/>

  <text x="2.4" y="27.2" fill="#9dbbd9" font-size="1.3px">NRF24L01</text>
</svg>
'''
guardar('nrf24l01', nrf, lnrf, anrf, [
    (nome, x, y, (['power'] if nome in ('GND', 'VCC') else ['spi']), '')
    for y, par in zip(linhas, nomes)
    for x, nome in zip(colunas, par)
])


cam = BASE + 'app/assets/pecas.json'
d = json.load(io.open(cam, encoding='utf-8'))
novos = {p['id'] for p in pecas}
d = [p for p in d if p['id'] not in novos] + pecas
d.sort(key=lambda p: p['id'])
io.open(cam, 'w', encoding='utf-8', newline='').write(json.dumps(d, ensure_ascii=False, indent=1))

print(f'\ncatalogo: {len(d)} pecas')
