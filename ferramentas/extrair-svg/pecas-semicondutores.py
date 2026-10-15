"""python ferramentas/extrair-svg/pecas-semicondutores.py

Transistores, MOSFETs, optoacoplador, fusivel e varistor (Alpha 24):

  - transistor-npn / transistor-pnp : TO-92 (o meio-cilindro preto, a face
    plana para a frente), pernas E, B, C no passo da protoboard; o modelo
    (2N2222A, 2N3904 / 2N2907A, 2N3906) escrito na face.
  - mosfet-n : TO-220 (G, D, S), IRLZ44N ou IRF520.
  - optoacoplador : o PC817 (DIP-4: anodo, catodo, emissor, coletor).
  - fusivel : o de vidro 5x20 com os terminais (axial, no passo do resistor).
  - varistor : o disco azul (radial).
"""
import io
import json

BASE = 'C:/Users/seraf/OneDrive/Desktop/SimuladorESP32/'
PX = 96 / 25.4
PASSO = 2.54
pecas = []


def gravar(nome, l, a, corpo):
    svg = ('<svg xmlns="http://www.w3.org/2000/svg" version="1.1"' + chr(10)
           + f'     width="{l}mm" height="{a}mm" viewBox="0 0 {l} {a}">' + chr(10) + corpo + chr(10) + '</svg>' + chr(10))
    io.open(BASE + f'app/assets/{nome}.svg', 'w', encoding='utf-8', newline='').write(svg)


def texto(x, y, t, tam, cor, peso='bold'):
    return (f'  <text x="{x:.2f}" y="{y:.2f}" fill="{cor}" font-size="{tam}px" text-anchor="middle" '
            f'font-family="Verdana" font-weight="{peso}">{t}</text>\n')


def furo(x, y):
    return (f'  <circle cx="{x:.2f}" cy="{y:.2f}" r="0.55" fill="#d9b44a" stroke="#8a6d1f" stroke-width="0.15"/>\n'
            f'  <circle cx="{x:.2f}" cy="{y:.2f}" r="0.22" fill="#2a2a2a"/>\n')


def pernas(xs, y0, y1):
    return ''.join(f'  <rect x="{x - 0.22:.2f}" y="{y0:.2f}" width="0.44" height="{y1 - y0:.2f}" fill="#b4b8be"/>\n' for x in xs)


def guardar(id_peca, l, a, pinos):
    pecas.append({
        'id': id_peca, 'largura': round(l * PX), 'altura': round(a * PX),
        'pinos': [{'nome': n, 'x': round(x * PX, 2), 'y': round(y * PX, 2), 'sinais': [], 'descricao': d} for n, x, y, d in pinos],
        'variantes': [], 'metades': False,
    })


# ---- TO-92 ----
l, a = 7.6, 13.0
xs = [l / 2 - PASSO, l / 2, l / 2 + PASSO]
yp = a - 0.6


def to92(modelo):
    s = pernas(xs, 5.0, yp)
    s += ('  <path d="M0.9 5.2 V2.2 Q0.9 0.3 3.8 0.3 Q6.7 0.3 6.7 2.2 V5.2 Z" fill="#1d1e22" stroke="#0a0a0b" stroke-width="0.12"/>\n'
          '  <rect x="1.3" y="0.9" width="5.0" height="0.5" rx="0.25" fill="#ffffff" opacity="0.1"/>\n')
    s += texto(l / 2, 2.6, modelo[:6], 0.85, '#d9dce0') + texto(l / 2, 3.7, modelo[6:], 0.85, '#d9dce0')
    for x, n in zip(xs, 'EBC'):
        s += texto(x, 6.6, n, 0.75, '#e6ecf5')
        s += furo(x, yp)
    return s


for tipo, modelos in [('transistor-npn', ['2N2222A', '2N3904']), ('transistor-pnp', ['2N2907A', '2N3906'])]:
    for i, m in enumerate(modelos):
        gravar(tipo if i == 0 else f'{tipo}-{m.lower()}', l, a, to92(m))
desc_npn = ['Emissor (E): no GND, no NPN que liga uma carga', 'Base (B): pelo resistor (1 kohm) no pino da placa',
            'Coletor (C): no - da carga (o relé, o motor, o LED)']
desc_pnp = ['Emissor (E): no + da alimentação, no PNP', 'Base (B): pelo resistor no pino (LOW liga)',
            'Coletor (C): no + da carga']
guardar('transistor-npn', l, a, [(n, x, yp, d) for n, x, d in zip('EBC', xs, desc_npn)])
guardar('transistor-pnp', l, a, [(n, x, yp, d) for n, x, d in zip('EBC', xs, desc_pnp)])

# ---- TO-220 (MOSFET) ----
l, a = 10.0, 27.0
xs2 = [l / 2 - PASSO, l / 2, l / 2 + PASSO]
yp2 = a - 0.6


def to220(modelo, linha2):
    s = ('  <path d="M0.2 1.2 Q0.2 0.2 1.2 0.2 H8.8 Q9.8 0.2 9.8 1.2 V6.4 H0.2 Z" fill="#c4c9d0" stroke="#7b8088" stroke-width="0.25"/>\n'
         '  <circle cx="5.0" cy="3.2" r="1.8" fill="#2a2c30" stroke="#7b8088" stroke-width="0.3"/>\n')
    s += pernas(xs2, 15.0, yp2)
    s += ('  <rect x="0.4" y="6.0" width="9.2" height="9.4" rx="0.4" fill="#1d1e22" stroke="#0a0a0b" stroke-width="0.25"/>\n')
    s += texto(5.0, 9.6, modelo, 1.35, '#d9dce0') + texto(5.0, 11.6, linha2, 0.9, '#8e949c', 'normal')
    for x, n in zip(xs2, 'GDS'):
        s += texto(x, 14.95, n, 0.68, '#c9ccd1')
        s += furo(x, yp2)
    return s


gravar('mosfet-n', l, a, to220('IRLZ44N', 'nivel logico'))
gravar('mosfet-n-irf520', l, a, to220('IRF520', 'gate 10 V'))
guardar('mosfet-n', l, a, [
    ('G', xs2[0], yp2, 'Gate: a tensão aqui liga o canal (o IRLZ44N liga com 3,3 V; o IRF520 pede 10 V). Um resistor de 10 kohm ao GND evita o gate solto'),
    ('D', xs2[1], yp2, 'Dreno: no - da carga'),
    ('S', xs2[2], yp2, 'Source: no GND'),
])

# ---- PC817 (DIP-4, visto de cima) ----
l, a = 6.6, 8.6
xs3 = [l / 2 - PASSO / 2, l / 2 + PASSO / 2]
s = ('  <rect x="0.9" y="1.4" width="4.8" height="5.8" rx="0.3" fill="#ece8df" stroke="#9a9488" stroke-width="0.15"/>\n'
     '  <circle cx="1.7" cy="2.3" r="0.35" fill="#9a9488"/>\n')
for x in xs3:
    s += f'  <rect x="{x - 0.3:.2f}" y="0.3" width="0.6" height="1.2" fill="#b4b8be"/>\n'
    s += f'  <rect x="{x - 0.3:.2f}" y="7.1" width="0.6" height="1.2" fill="#b4b8be"/>\n'
s += texto(l / 2, 4.6, 'PC817', 0.95, '#3a3d42')
pinos = [('A', xs3[0], 8.3, 'Anodo do LED de dentro (pelo resistor no pino da placa)'),
         ('K', xs3[1], 8.3, 'Catodo do LED (no GND da placa)'),
         ('C', xs3[1], 0.3, 'Coletor do fototransistor (o lado isolado: no - da carga)'),
         ('E', xs3[0], 0.3, 'Emissor do fototransistor (no GND do outro circuito)')]
for n, x, y, _ in pinos:
    s += furo(x, y)
gravar('optoacoplador', l, a, s)
guardar('optoacoplador', l, a, pinos)

# ---- fusivel de vidro 5x20, axial ----
l, a = 15.645, 4.0
s = (f'  <rect x="0" y="{a / 2 - 0.3:.2f}" width="{l}" height="0.6" fill="#b4b8be"/>\n'
     '  <rect x="3.0" y="0.6" width="9.6" height="2.8" rx="0.3" fill="#dfe8ee" fill-opacity="0.55" stroke="#8a9aa6" stroke-width="0.12"/>\n'
     '  <rect x="3.0" y="0.5" width="1.6" height="3.0" rx="0.2" fill="#c9ccd1" stroke="#80858c" stroke-width="0.12"/>\n'
     '  <rect x="11.0" y="0.5" width="1.6" height="3.0" rx="0.2" fill="#c9ccd1" stroke="#80858c" stroke-width="0.12"/>\n'
     '  <path d="M4.6 2.0 C6.0 1.4 7.0 2.6 7.8 2.0 C8.6 1.4 9.6 2.6 11.0 2.0" fill="none" stroke="#6a6d73" stroke-width="0.12"/>\n')
# (o desenho do fusivel e do varistor agora sai de pecas-passivos.py, um por modelo)
guardar('fusivel', l, a, [('1', 0, a / 2, 'Terminal'), ('2', l - 0.05, a / 2, 'Terminal')])

# ---- varistor ----
l, a = 8.0, 12.0
xs4 = [l / 2 - PASSO, l / 2 + PASSO]
s = pernas(xs4, 6.0, a - 0.3)
s += ('  <ellipse cx="4.0" cy="3.7" rx="3.7" ry="3.5" fill="#1f5fb4" stroke="#123e7a" stroke-width="0.15"/>\n'
      '  <ellipse cx="3.1" cy="2.6" rx="1.4" ry="0.9" fill="#ffffff" opacity="0.2"/>\n')
s += texto(4.0, 3.8, '14D', 1.2, '#dfe7f7') + texto(4.0, 5.1, '270K', 0.9, '#cfe0f7')
guardar('varistor', l, a, [('1', xs4[0], a - 0.3, 'Terminal (sem polaridade)'), ('2', xs4[1], a - 0.3, 'Terminal')])

cam = BASE + 'app/assets/pecas.json'
d = json.load(io.open(cam, encoding='utf-8'))
novos = {p['id'] for p in pecas}
d = [p for p in d if p['id'] not in novos] + pecas
d.sort(key=lambda p: p['id'])
io.open(cam, 'w', encoding='utf-8', newline='').write(json.dumps(d, ensure_ascii=False, indent=1))
print(f'catalogo: {len(d)} pecas')
