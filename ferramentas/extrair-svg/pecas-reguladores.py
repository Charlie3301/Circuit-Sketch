"""python ferramentas/extrair-svg/pecas-reguladores.py

Os reguladores de tensao (Alpha 24), no tamanho de verdade:

  - regulador-7805 : o 78xx no encapsulamento TO-220 (a aba de metal com o
                     furo, o corpo preto e as tres pernas: IN, GND, OUT). O
                     modelo escolhido na barra (7805, 7806, 7808, 7809, 7812,
                     7815) troca o que esta escrito nele:
                     regulador-7805-<modelo>.svg.
  - ams1117        : o modulo AMS1117 (SOT-223 numa plaquinha azul, com os
                     capacitores e o LED): VIN, GND e VOUT. O de 5 V e o
                     ams1117-5v0.svg.
  - lm2596         : o modulo step-down LM2596 (43 x 21 mm): o indutor, o
                     chip, os dois eletroliticos e o trimpot azul; IN+/IN- de
                     um lado, OUT+/OUT- do outro.
  - mt3608         : o modulo step-up MT3608 (37 x 17 mm): VIN+/VIN- e
                     VOUT+/VOUT-.
"""
import io
import json

BASE = 'C:/Users/seraf/OneDrive/Desktop/SimuladorESP32/'
PX = 96 / 25.4
PASSO = 2.54

pecas = []


def gravar_svg(nome, l, a, corpo):
    svg = ('<svg xmlns="http://www.w3.org/2000/svg" version="1.1"' + chr(10)
           + f'     width="{l}mm" height="{a}mm" viewBox="0 0 {l} {a}">' + chr(10)
           + corpo + chr(10) + '</svg>' + chr(10))
    io.open(BASE + f'app/assets/{nome}.svg', 'w', encoding='utf-8', newline='').write(svg)


def guardar(id_peca, larg_mm, alt_mm, pinos):
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


def texto(x, y, t, tam=1.2, cor='#f2f2f2', ancora='middle', peso='bold'):
    return (f'  <text x="{x:.2f}" y="{y:.2f}" fill="{cor}" font-size="{tam}px" text-anchor="{ancora}"\n'
            f'        font-family="Verdana" font-weight="{peso}">{t}</text>\n')


def furo(x, y, r=0.75):
    """O furo metalizado de uma placa (onde o fio entra)."""
    return (f'  <circle cx="{x:.2f}" cy="{y:.2f}" r="{r:.2f}" fill="#d9b44a" stroke="#8a6d1f" stroke-width="0.2"/>\n'
            f'  <circle cx="{x:.2f}" cy="{y:.2f}" r="{r * 0.45:.2f}" fill="#2a2a2a"/>\n')


def placa_azul(l, a):
    return (f'  <rect x="0.2" y="0.2" width="{l - 0.4:.2f}" height="{a - 0.4:.2f}" rx="1.0" fill="#1f5fb4" stroke="#123e7a" stroke-width="0.35"/>\n'
            f'  <rect x="0.9" y="0.9" width="{l - 1.8:.2f}" height="{a - 1.8:.2f}" rx="0.7" fill="none" stroke="#ffffff" stroke-opacity="0.10" stroke-width="0.3"/>\n')


def trimpot(x, y, l=9.5, a=4.8):
    """O trimpot multivoltas azul, com o parafuso de latao numa ponta."""
    return (f'  <rect x="{x:.2f}" y="{y:.2f}" width="{l:.2f}" height="{a:.2f}" rx="0.5" fill="#2f6fd6" stroke="#173a73" stroke-width="0.3"/>\n'
            f'  <rect x="{x + 0.6:.2f}" y="{y + 0.6:.2f}" width="{l - 1.2:.2f}" height="{a - 1.2:.2f}" rx="0.3" fill="#3a7ce0"/>\n'
            f'  <circle cx="{x + l - 1.4:.2f}" cy="{y + a / 2:.2f}" r="1.05" fill="#d8b660" stroke="#7d6420" stroke-width="0.25"/>\n'
            f'  <path d="M{x + l - 2.2:.2f} {y + a / 2:.2f} H{x + l - 0.6:.2f}" stroke="#7d6420" stroke-width="0.3"/>\n')


def eletrolitico(cx, cy, r, rotulo='220'):
    """O capacitor eletrolitico visto de cima: a lata, a cruz e a faixa do negativo."""
    return (f'  <circle cx="{cx:.2f}" cy="{cy:.2f}" r="{r:.2f}" fill="#1c1d21" stroke="#0b0b0c" stroke-width="0.25"/>\n'
            f'  <circle cx="{cx:.2f}" cy="{cy:.2f}" r="{r * 0.82:.2f}" fill="#b9bec6" stroke="#7d828a" stroke-width="0.2"/>\n'
            f'  <path d="M{cx - r * 0.5:.2f} {cy:.2f} H{cx + r * 0.5:.2f} M{cx:.2f} {cy - r * 0.5:.2f} V{cy + r * 0.5:.2f}" stroke="#7d828a" stroke-width="0.25"/>\n'
            f'  <path d="M{cx - r * 0.82:.2f} {cy:.2f} A{r * 0.82:.2f} {r * 0.82:.2f} 0 0 1 {cx - r * 0.3:.2f} {cy - r * 0.76:.2f}" fill="none" stroke="#e8e8e8" stroke-width="0.45"/>\n'
            + texto(cx + r * 0.05, cy + r + 1.1, rotulo, 0.85, '#cfe0f7', peso='normal'))


def smd(x, y, l, a, cor='#b9875a'):
    return f'  <rect x="{x:.2f}" y="{y:.2f}" width="{l:.2f}" height="{a:.2f}" rx="0.15" fill="{cor}" stroke="#4a3622" stroke-width="0.12"/>\n'


# =====================================================================
#  78xx no TO-220 (de frente, a aba para cima)
# =====================================================================
MODELOS_78 = {'7805': 5, '7806': 6, '7808': 8, '7809': 9, '7812': 12, '7815': 15}
L78, A78 = 10.0, 27.0
XS_78 = [L78 / 2 - PASSO, L78 / 2, L78 / 2 + PASSO]
Y_PERNA = A78 - 0.6


def to220(modelo):
    corpo = (f'  <!-- a aba de metal (o dissipador do chip), com o furo do parafuso -->\n'
             f'  <path d="M0.2 1.2 Q0.2 0.2 1.2 0.2 H{L78 - 1.2:.2f} Q{L78 - 0.2:.2f} 0.2 {L78 - 0.2:.2f} 1.2 V6.4 H0.2 Z" fill="#c4c9d0" stroke="#7b8088" stroke-width="0.25"/>\n'
             f'  <rect x="0.7" y="0.7" width="{L78 - 1.4:.2f}" height="1.2" rx="0.4" fill="#e4e7eb" opacity="0.8"/>\n'
             f'  <circle cx="{L78 / 2:.2f}" cy="3.2" r="1.8" fill="#2a2c30" stroke="#7b8088" stroke-width="0.3"/>\n'
             f'  <!-- as pernas: IN, GND, OUT -->\n')
    for x in XS_78:
        corpo += (f'  <rect x="{x - 0.45:.2f}" y="15.0" width="0.9" height="{Y_PERNA - 15.0 + 0.4:.2f}" fill="#cfd3d8" stroke="#80858c" stroke-width="0.15"/>\n'
                  f'  <rect x="{x - 0.75:.2f}" y="15.0" width="1.5" height="2.0" fill="#cfd3d8" stroke="#80858c" stroke-width="0.15"/>\n')
    corpo += (f'  <!-- o corpo de epoxi -->\n'
              f'  <rect x="0.4" y="6.0" width="{L78 - 0.8:.2f}" height="9.4" rx="0.4" fill="#1d1e22" stroke="#0a0a0b" stroke-width="0.25"/>\n'
              f'  <rect x="0.9" y="6.5" width="{L78 - 1.8:.2f}" height="0.8" rx="0.3" fill="#ffffff" opacity="0.08"/>\n')
    corpo += texto(L78 / 2, 9.4, 'L' + modelo, 1.75, '#d9dce0')
    corpo += texto(L78 / 2, 11.5, 'CV', 1.2, '#b9bec6')
    corpo += texto(L78 / 2, 13.1, f'{MODELOS_78[modelo]} V  1,5 A', 0.85, '#8e949c', peso='normal')
    for x, n in zip(XS_78, ['IN', 'GND', 'OUT']):
        corpo += texto(x, 14.95, n, 0.68, '#c9ccd1')
    for x in XS_78:
        corpo += furo(x, Y_PERNA, 0.62)
    return corpo


for modelo in MODELOS_78:
    gravar_svg('regulador-7805' if modelo == '7805' else f'regulador-7805-{modelo}', L78, A78, to220(modelo))
guardar('regulador-7805', L78, A78, [
    ('IN', XS_78[0], Y_PERNA, ['power'], 'Entrada: o + da bateria ou da fonte. Precisa de ~2 V a mais que a saida (o 7805 pede 7 V para dar 5 V); maximo 35 V'),
    ('GND', XS_78[1], Y_PERNA, ['power'], 'GND: o - da bateria, o GND da placa e o da carga, todos aqui'),
    ('OUT', XS_78[2], Y_PERNA, ['power'], 'Saida regulada (5 V no 7805, 12 V no 7812...), ate 1,5 A. O que sobra da entrada vira calor'),
])


# =====================================================================
#  AMS1117 (o modulo de 3,3 V, ou o de 5 V)
# =====================================================================
LA, AA = 16.5, 12.0
XS_A = [LA / 2 - PASSO, LA / 2, LA / 2 + PASSO]
Y_A = AA - 1.4


def ams1117(volts):
    corpo = placa_azul(LA, AA)
    # o SOT-223: a aba larga em cima, as tres perninhas embaixo
    corpo += (f'  <rect x="{LA / 2 - 1.6:.2f}" y="1.0" width="3.2" height="1.3" fill="#cfd3d8" stroke="#80858c" stroke-width="0.12"/>\n'
              f'  <rect x="{LA / 2 - 3.3:.2f}" y="2.2" width="6.6" height="3.6" rx="0.25" fill="#1d1e22" stroke="#0a0a0b" stroke-width="0.2"/>\n')
    for dx in (-2.3, 0, 2.3):
        corpo += f'  <rect x="{LA / 2 + dx - 0.35:.2f}" y="5.8" width="0.7" height="0.9" fill="#cfd3d8" stroke="#80858c" stroke-width="0.1"/>\n'
    corpo += texto(LA / 2, 3.9, 'AMS1117', 0.95, '#d9dce0')
    corpo += texto(LA / 2, 5.25, volts, 0.95, '#b9bec6')
    # os capacitores de tantalo (amarelos, faixa do +) e o LED de energia
    corpo += smd(1.6, 2.0, 2.2, 3.2, '#e0b43a') + f'  <rect x="1.6" y="2.0" width="2.2" height="0.7" fill="#8a6a16"/>\n'
    corpo += smd(LA - 3.8, 2.0, 2.2, 3.2, '#e0b43a') + f'  <rect x="{LA - 3.8:.2f}" y="2.0" width="2.2" height="0.7" fill="#8a6a16"/>\n'
    corpo += smd(1.7, 6.3, 1.6, 0.9, '#f2f2ee') + f'  <rect x="2.05" y="6.38" width="0.9" height="0.74" fill="#7a1414"/>\n'
    corpo += smd(LA - 3.4, 6.3, 1.6, 0.9, '#2a2a2a')
    for x, n in zip(XS_A, ['VIN', 'GND', 'VOUT']):
        corpo += texto(x, Y_A - 1.35, n, 0.7, '#e6ecf5')
        corpo += furo(x, Y_A, 0.62)
    return corpo


gravar_svg('ams1117', LA, AA, ams1117('3.3'))
gravar_svg('ams1117-5v0', LA, AA, ams1117('5.0'))
guardar('ams1117', LA, AA, [
    ('VIN', XS_A[0], Y_A, ['power'], 'Entrada: 4,5 a 12 V (no maximo 15 V). Precisa de ~1,1 V a mais que a saida'),
    ('GND', XS_A[1], Y_A, ['power'], 'GND: o - da entrada e o da saida, os dois aqui'),
    ('VOUT', XS_A[2], Y_A, ['power'], 'Saida regulada: 3,3 V (ou 5 V no AMS1117-5.0), ate ~0,8 A'),
])


# =====================================================================
#  LM2596 (step-down)
# =====================================================================
LL, AL = 43.0, 21.0
PINOS_L = [('IN+', 2.6, 3.2), ('IN-', 2.6, AL - 3.2), ('OUT+', LL - 2.6, 3.2), ('OUT-', LL - 2.6, AL - 3.2)]
corpo = placa_azul(LL, AL)
# o indutor (a bobina blindada preta, "330" = 33 uH)
corpo += (f'  <rect x="5.4" y="4.2" width="12.6" height="12.6" rx="1.6" fill="#232428" stroke="#0b0b0c" stroke-width="0.3"/>\n'
          f'  <circle cx="11.7" cy="10.5" r="4.6" fill="#2c2d32" stroke="#3a3c41" stroke-width="0.3"/>\n')
corpo += texto(11.7, 11.1, '330', 1.6, '#c9ccd1')
# o chip (TO-263: a aba larga embaixo)
corpo += (f'  <rect x="20.2" y="11.6" width="8.4" height="2.0" fill="#cfd3d8" stroke="#80858c" stroke-width="0.15"/>\n'
          f'  <rect x="20.0" y="4.0" width="8.8" height="7.8" rx="0.3" fill="#1d1e22" stroke="#0a0a0b" stroke-width="0.2"/>\n')
corpo += texto(24.4, 7.0, 'LM2596S', 0.95, '#d9dce0') + texto(24.4, 8.5, 'ADJ', 0.9, '#b9bec6')
# o diodo e os dois eletroliticos
corpo += smd(21.0, 15.2, 4.0, 2.6, '#26282c') + f'  <rect x="21.0" y="15.2" width="0.8" height="2.6" fill="#b9bec6"/>\n'
corpo += eletrolitico(33.4, 6.4, 3.15) + eletrolitico(33.4, 14.6, 3.15)
# o trimpot (a saida se ajusta nele)
corpo += trimpot(8.2, AL - 3.3, 9.5, 2.6)
corpo += texto(27.5, AL - 0.9, 'DC-DC STEP DOWN', 0.95, '#cfe0f7')
for n, x, y in PINOS_L:
    corpo += furo(x, y, 0.95)
    corpo += texto(x + (2.2 if x < LL / 2 else -2.2), y + 0.4, n, 0.95, '#e6ecf5', 'start' if x < LL / 2 else 'end')
gravar_svg('lm2596', LL, AL, corpo)
guardar('lm2596', LL, AL, [
    ('IN+', PINOS_L[0][1], PINOS_L[0][2], ['power'], 'Entrada +: 4,5 a 40 V (precisa de ~1,5 V a mais que a saida)'),
    ('IN-', PINOS_L[1][1], PINOS_L[1][2], ['power'], 'Entrada - (GND): e o mesmo GND da saida'),
    ('OUT+', PINOS_L[2][1], PINOS_L[2][2], ['power'], 'Saida +: a tensao ajustada no trimpot (1,25 a 35 V; 5 V de fabrica aqui), ate 3 A'),
    ('OUT-', PINOS_L[3][1], PINOS_L[3][2], ['power'], 'Saida - (GND): ligue no GND da placa'),
])


# =====================================================================
#  MT3608 (step-up)
# =====================================================================
LM, AM = 37.0, 17.0
PINOS_M = [('VIN+', 2.4, 3.4), ('VIN-', 2.4, AM - 3.4), ('VOUT+', LM - 2.4, 3.4), ('VOUT-', LM - 2.4, AM - 3.4)]
corpo = placa_azul(LM, AM)
# o indutor (22 uH), o chip SOT-23-6, o diodo e os capacitores ceramicos
corpo += (f'  <rect x="7.8" y="3.4" width="6.6" height="6.6" rx="1.0" fill="#3a3c41" stroke="#0b0b0c" stroke-width="0.25"/>\n'
          f'  <circle cx="11.1" cy="6.7" r="2.4" fill="#45474d" stroke="#2a2b2f" stroke-width="0.2"/>\n')
corpo += texto(11.1, 7.2, '220', 1.15, '#c9ccd1')
corpo += smd(16.4, 4.0, 3.0, 1.7, '#1d1e22') + texto(17.9, 7.1, 'MT3608', 0.8, '#cfe0f7')
for dx in (0.3, 1.2, 2.1):
    corpo += f'  <rect x="{16.4 + dx:.2f}" y="3.4" width="0.45" height="0.6" fill="#cfd3d8"/>\n'
    corpo += f'  <rect x="{16.4 + dx:.2f}" y="5.7" width="0.45" height="0.6" fill="#cfd3d8"/>\n'
corpo += smd(21.2, 3.6, 3.2, 2.0, '#26282c') + f'  <rect x="21.2" y="3.6" width="0.7" height="2.0" fill="#b9bec6"/>\n'
corpo += smd(25.4, 3.2, 2.2, 1.4) + smd(25.4, 5.6, 2.2, 1.4) + smd(16.6, 8.4, 2.0, 1.2)
corpo += trimpot(16.0, AM - 6.6, 9.5, 4.0)
corpo += texto(LM / 2 - 3.0, AM - 0.9, 'DC-DC STEP UP', 0.85, '#cfe0f7')
for n, x, y in PINOS_M:
    corpo += furo(x, y, 0.9)
    corpo += texto(x + (2.0 if x < LM / 2 else -2.0), y + 0.4, n, 0.85, '#e6ecf5', 'start' if x < LM / 2 else 'end')
gravar_svg('mt3608', LM, AM, corpo)
guardar('mt3608', LM, AM, [
    ('VIN+', PINOS_M[0][1], PINOS_M[0][2], ['power'], 'Entrada +: 2 a 24 V (uma celula de litio, duas pilhas...)'),
    ('VIN-', PINOS_M[1][1], PINOS_M[1][2], ['power'], 'Entrada - (GND): e o mesmo GND da saida'),
    ('VOUT+', PINOS_M[2][1], PINOS_M[2][2], ['power'], 'Saida +: a tensao ajustada no trimpot (ate 28 V; 5 V de fabrica aqui). So SOBE: com a entrada acima, sai a entrada'),
    ('VOUT-', PINOS_M[3][1], PINOS_M[3][2], ['power'], 'Saida - (GND): ligue no GND da placa'),
])


cam = BASE + 'app/assets/pecas.json'
d = json.load(io.open(cam, encoding='utf-8'))
novos = {p['id'] for p in pecas}
d = [p for p in d if p['id'] not in novos] + pecas
d.sort(key=lambda p: p['id'])
io.open(cam, 'w', encoding='utf-8', newline='').write(json.dumps(d, ensure_ascii=False, indent=1))
print(f'\ncatalogo: {len(d)} pecas')
