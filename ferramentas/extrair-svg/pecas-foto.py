"""python ferramentas/extrair-svg/pecas-foto.py"""
import io
import json

BASE = 'C:/Users/seraf/OneDrive/Desktop/SimuladorESP32/'
PX = 96 / 25.4
PASSO = 2.54

pecas = []


def guardar(id_peca, svg, larg_mm, alt_mm, pinos):
    # o LDR e desenhado por pecas-passivos.py (Alpha 25); aqui so o catalogo
    if id_peca != 'ldr':
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


lldr, aldr = 6.4, 14.0
meio = lldr / 2
p1, p2 = meio - PASSO / 2, meio + PASSO / 2

zigue = ('M1.45 1.95 h3.5 v0.85 h-3.5 v0.85 h3.5 v0.85 h-3.5 v0.85 h3.5 v0.85 h-3.5')

ldr = f'''<svg xmlns="http://www.w3.org/2000/svg" version="1.1"
     width="{lldr}mm" height="{aldr}mm" viewBox="0 0 {lldr} {aldr}">

  <g stroke="#9aa0a6" stroke-width="0.42" stroke-linecap="round" fill="none">
    <path d="M{p1:.2f} 5.6 V{aldr - 0.4:.2f}"/>
    <path d="M{p2:.2f} 5.6 V{aldr - 0.4:.2f}"/>
  </g>

  <ellipse cx="{meio:.2f}" cy="3.3" rx="3.0" ry="3.0" fill="#d9cdb4"/>
  <ellipse cx="{meio:.2f}" cy="3.3" rx="3.0" ry="3.0"
           fill="none" stroke="#a8998a" stroke-width="0.25"/>
  <ellipse cx="{meio:.2f}" cy="3.3" rx="2.45" ry="2.45" fill="#efe6d2"/>

  <path d="{zigue}" fill="none" stroke="#8a6a4a" stroke-width="0.45"
        stroke-linecap="square"/>

  <ellipse cx="{meio:.2f}" cy="3.3" rx="3.0" ry="3.0"
           fill="none" stroke="#9a8f78" stroke-width="0.3"/>
  <path d="M{meio - 1.6:.2f} 1.35 a2.4 2.4 0 0 1 2.6 0"
        fill="none" stroke="#ffffff" stroke-width="0.35" opacity="0.5"/>
</svg>
'''
guardar('ldr', ldr, lldr, aldr, [
    ('1', p1, aldr - 0.4, [], 'uma perna'),
    ('2', p2, aldr - 0.4, [], 'a outra perna'),
])


ldht, adht = 12.0, 18.0
xs = [ldht / 2 - PASSO, ldht / 2, ldht / 2 + PASSO]
nomes = ['VCC', 'DATA', 'GND']

pernas = '\n'.join(
    f'    <path d="M{x:.2f} 13.4 V{adht - 0.4:.2f}"/>' for x in xs)

furos = '\n'.join(
    f'    <circle cx="{2.1 + (i % 4) * 2.6:.2f}" cy="{2.4 + (i // 4) * 2.5:.2f}" r="0.62"/>'
    for i in range(16))

dht11 = f'''<svg xmlns="http://www.w3.org/2000/svg" version="1.1"
     width="{ldht}mm" height="{adht}mm" viewBox="0 0 {ldht} {adht}"
     font-family="monospace" text-anchor="middle">

  <g stroke="#b9bec4" stroke-width="0.45" stroke-linecap="round" fill="none">
{pernas}
  </g>

  <rect x="0.5" y="0.5" width="{ldht - 1}" height="12.6" rx="0.7" fill="#2f74c0"/>
  <rect x="0.5" y="0.5" width="{ldht - 1}" height="12.6" rx="0.7"
        fill="none" stroke="#215a97" stroke-width="0.35"/>
  <rect x="1.1" y="1.1" width="{ldht - 2.2}" height="8.6" rx="0.4" fill="#2a68ad"/>

  <g fill="#1d4a7d">
{furos}
  </g>

  <rect x="1.1" y="10.3" width="{ldht - 2.2}" height="2.2" rx="0.3" fill="#2a68ad"/>
  <text x="{ldht / 2:.2f}" y="12.0" fill="#cfe0f2" font-size="1.5px">DHT11</text>
</svg>
'''
guardar('dht11', dht11, ldht, adht, [
    (nome, x, adht - 0.4, (['power'] if nome in ('VCC', 'GND') else []), '')
    for nome, x in zip(nomes, xs)
])


cam = BASE + 'app/assets/pecas.json'
d = json.load(io.open(cam, encoding='utf-8'))
novos = {p['id'] for p in pecas}
d = [p for p in d if p['id'] not in novos] + pecas
d.sort(key=lambda p: p['id'])
io.open(cam, 'w', encoding='utf-8', newline='').write(json.dumps(d, ensure_ascii=False, indent=1))

print(f'\ncatalogo: {len(d)} pecas')
