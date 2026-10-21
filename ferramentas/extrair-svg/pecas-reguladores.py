"""python ferramentas/extrair-svg/pecas-reguladores.py

Os reguladores de tensao, redesenhados na Alpha 25 no estilo das placas
(as primitivas de placas-arduino.py), no tamanho real:

  - regulador-7805 : o 78xx no TO-220, nas medidas do JEDEC (a aba de metal com
                     o furo de 3,7 mm, o corpo de 10 x 9 mm e as tres pernas de
                     2,54 mm: IN, GND, OUT). Um SVG por modelo (7805 a 7815):
                     regulador-7805-<modelo>.svg.
  - ams1117        : o modulo AMS1117 de 25 x 11 mm (o SOT-223, os capacitores,
                     o LED): VIN e GND numa ponta, VOUT e GND na outra -- quatro
                     pinos, como na placa. O de 5 V e o ams1117-5v0.svg.
  - lm2596         : o modulo step-down de 43 x 21 mm, medido numa foto
                     (Wikimedia Commons, de Matthew Berardi, CC BY-SA 4.0): as
                     ilhas nos cantos, os eletroliticos SMD de entrada e de
                     saida, o LM2596S (TO-263), o indutor de 33 uH, o trimpot
                     3296 em pe e o SS34.
  - mt3608         : o modulo step-up de 37,2 x 17,3 mm, pelo desenho de cotas
                     da components101: VIN do lado do indutor, VOUT do lado do
                     trimpot.

O que acende (o LED do AMS1117) o editor desenha por cima.
"""
import importlib.util
import io
import json
import math
import os

AQUI = os.path.dirname(os.path.abspath(__file__))
_spec = importlib.util.spec_from_file_location('placas_arduino', os.path.join(AQUI, 'placas-arduino.py'))
pa = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(pa)

Desenho, T, f = pa.Desenho, pa.T, pa.f
SERIGRAFIA, ESTANHO, PRETO_CI, MARCA_CI = pa.SERIGRAFIA, pa.ESTANHO, pa.PRETO_CI, pa.MARCA_CI
A = 96 / 25.4
AZUL, AZUL_BORDA = '#1f5fb4', '#123e7a'
CAPACITOR, RESISTOR = '#b7956a', '#1e1f22'

DEFS_EXTRA = '''  <radialGradient id="lata-topo" cx="0.4" cy="0.35" r="0.75"><stop offset="0" stop-color="#f1f3f5"/><stop offset="0.6" stop-color="#c3c8ce"/><stop offset="1" stop-color="#8a9097"/></radialGradient>
  <linearGradient id="niquel" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#e9ecef"/><stop offset="0.5" stop-color="#c2c7cd"/><stop offset="1" stop-color="#959ba2"/></linearGradient>
  <linearGradient id="trimpot" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#4f8be8"/><stop offset="1" stop-color="#2459b8"/></linearGradient>
  <radialGradient id="latao" cx="0.4" cy="0.35" r="0.7"><stop offset="0" stop-color="#f6e3a0"/><stop offset="0.6" stop-color="#d1a845"/><stop offset="1" stop-color="#8f6d1f"/></radialGradient>
'''

pecas = []


def svg_de(d, L, H, sobra_x=0.0, sobra_y=0.0):
    w, h = L * A + 2 * sobra_x, H * A + 2 * sobra_y
    extra = f' data-sobra="{f(sobra_x)} {f(sobra_y)}"' if sobra_x or sobra_y else ''
    return (f'<svg xmlns="http://www.w3.org/2000/svg" version="1.1" width="{f(w)}px" height="{f(h)}px"{extra} '
            f'viewBox="0 0 {f(w)} {f(h)}">\n<defs>\n{pa.DEFS}{DEFS_EXTRA}</defs>\n' + ''.join(d.partes) + '</svg>\n')


def gravar(nome, svg):
    io.open(pa.BASE + f'app/assets/{nome}.svg', 'w', encoding='utf-8', newline='').write(svg)


def guardar(id_peca, L, H, pinos):
    pecas.append({'id': id_peca, 'largura': round(L * A), 'altura': round(H * A),
                  'pinos': [{'nome': n, 'x': round(x * A, 2), 'y': round(y * A, 2), 'sinais': s, 'descricao': dsc}
                            for n, x, y, s, dsc in pinos],
                  'variantes': [], 'metades': False})
    print(f'{id_peca}: {round(L * A)}x{round(H * A)} px, {len(pinos)} pinos')


def placa(d, L, H, rx=0.8):
    d.ret(0, 0, L, H, AZUL, rx=rx, stroke=AZUL_BORDA, sw=0.15)
    d.ret(0, 0, L, H, 'url(#luz)', rx=rx)


def dois(d, cx, cy, vertical, corpo, comp=2.0, larg=1.25, marca=None):
    """Um 0603/0805/1206 com os terminais estanhados."""
    w, h = (larg, comp) if vertical else (comp, larg)
    d.ret_c(cx, cy, w, h, corpo, rx=0.08)
    d.ret_c(cx, cy, w, h, 'url(#luz)', rx=0.08)
    t = comp * 0.22
    if vertical:
        d.ret(cx - w / 2, cy - h / 2, w, t, 'url(#metal)')
        d.ret(cx - w / 2, cy + h / 2 - t, w, t, 'url(#metal)')
    else:
        d.ret(cx - w / 2, cy - h / 2, t, h, 'url(#metal)')
        d.ret(cx + w / 2 - t, cy - h / 2, t, h, 'url(#metal)')
    if marca:
        d.texto(cx, cy, marca, min(w, h) * 0.45, MARCA_CI, ang=90 if vertical else 0, alinhar='center',
                peso='normal')


def furo(d, x, y, r):
    d.circ(x, y, r, '#15171b')


def pino_header(d, x, y):
    """O pino da barra, visto de cima (o plastico preto e a ponta dourada)."""
    d.ret_c(x, y, 2.4, 2.4, 'url(#plastico)', rx=0.3)
    d.ret_c(x, y, 0.84, 0.84, '#7a5c18')
    d.ret_c(x, y, 0.64, 0.64, 'url(#ouro)')


def eletrolitico_smd(d, cx, cy, r, linhas):
    """O eletrolitico SMD (o "V-chip"), visto de cima: a base de plastico com os
    cantos chanfrados do +, os dois terminais, a lata de aluminio com a marcacao
    preta do - (embaixo) e o valor impresso em colunas."""
    b = r + 0.15
    k = 1.3
    d.poli([(cx - b + k, cy - b), (cx + b - k, cy - b), (cx + b, cy - b + k), (cx + b, cy + b), (cx - b, cy + b),
            (cx - b, cy - b + k)], '#2a2b2f', stroke='#111214', sw=0.08)
    for s in (-1, 1):
        d.ret_c(cx, cy + s * (b + 0.15), 1.0, 0.7, ESTANHO, rx=0.1)
    d.circ(cx, cy, r, 'url(#lata-topo)', stroke='#7d828a', sw=0.12)
    h = r * 0.5
    w = math.sqrt(r * r - h * h)
    d.caminho([('M', cx - w, cy + h), ('A', r, 0, 0, cx + w, cy + h), ('Z',)], '#1b1c20', op=0.92)
    for k, t in enumerate(linhas):
        d.texto(cx + (k - (len(linhas) - 1) / 2) * r * 0.42, cy - r * 0.18, t, r * 0.2, '#1c1d21', ang=-90,
                alinhar='center', peso='bold')


def sma(d, cx, cy, marca, banda='esq', larg=4.3, alt=2.6, passo=1.75):
    """O diodo SMA/SMB (o SS34): os terminais, o corpo e a faixa do catodo."""
    for s in (-1, 1):
        d.ret_c(cx + s * passo, cy, 1.4, alt * 0.6, ESTANHO, rx=0.1)
    d.ret_c(cx, cy, larg, alt, PRETO_CI, rx=0.2)
    d.ret_c(cx, cy, larg, alt, 'url(#luz)', rx=0.2)
    xb = cx - larg / 2 + 0.45 if banda == 'esq' else cx + larg / 2 - 0.45
    d.ret_c(xb, cy, 0.45, alt, '#9aa1a8')
    d.texto(cx + (0.3 if banda == 'esq' else -0.3), cy, marca, alt * 0.3, MARCA_CI, alinhar='center', peso='normal')


def trimpot_em_pe(d, x, y, w, h, parafuso_x):
    """O trimpot multivoltas (3296W) em pe: de cima, o topo azul estreito e o parafuso de latao."""
    d.ret(x, y, w, h, 'url(#trimpot)', rx=0.35, stroke='#173a73', sw=0.1)
    d.ret(x + 0.4, y + 0.4, w - 0.8, h - 0.8, None, rx=0.25, stroke='#7aa8f0', sw=0.08)
    d.circ(parafuso_x, y + h / 2, 1.1, 'url(#latao)', stroke='#7d6420', sw=0.1)
    d.linha(parafuso_x - 0.8, y + h / 2, parafuso_x + 0.8, y + h / 2, '#6b5418', 0.28)


def trimpot_deitado(d, x, y, lado, parafuso):
    """O 3296 deitado (a face grande para cima): o corpo azul, o texto e o parafuso de latao num canto."""
    d.ret(x, y, lado, lado, 'url(#trimpot)', rx=0.4, stroke='#173a73', sw=0.1)
    d.ret(x + 0.5, y + 0.5, lado - 1.0, lado - 1.0, None, rx=0.3, stroke='#7aa8f0', sw=0.08)
    d.texto(x + lado / 2, y + lado * 0.45, '3296', lado * 0.16, '#c7dbf8', alinhar='center', peso='bold')
    d.texto(x + lado / 2, y + lado * 0.7, 'W 104', lado * 0.11, '#c7dbf8', alinhar='center', peso='normal')
    px, py = parafuso
    d.ret_c(px, py, 1.6, 1.2, 'url(#latao)', rx=0.3)
    d.circ(px, py + 0.5, 0.75, 'url(#latao)', stroke='#7d6420', sw=0.08)
    d.linha(px - 0.55, py + 0.5, px + 0.55, py + 0.5, '#6b5418', 0.22)


def indutor(d, cx, cy, w, h, raio, marca):
    """O indutor blindado (CD54/CDRH): o corpo de ferrite com o topo redondo e o valor."""
    d.ret_c(cx, cy, w, h, '#2b2c30', rx=min(w, h) * 0.15, stroke='#0b0b0c', sw=0.12)
    d.ret_c(cx, cy, w, h, 'url(#luz)', rx=min(w, h) * 0.15)
    d.circ(cx, cy, raio, '#3a3c41', stroke='#1c1d20', sw=0.25)
    d.texto(cx, cy, marca, raio * 0.45, '#c9ccd1', alinhar='center', peso='bold')


# =====================================================================
#  78xx no TO-220 (de frente, a aba para cima)
# =====================================================================
MODELOS_78 = ['7805', '7806', '7808', '7809', '7812', '7815']
L78, A78 = 10.0, 27.0
XS_78 = [L78 / 2 - 2.54, L78 / 2, L78 / 2 + 2.54]
Y_PERNA = A78 - 0.6


def to220(modelo):
    d = Desenho(T(A, 0, 0, A, 0, 0))
    # a aba de metal (o dissipador), com o furo do parafuso e os entalhes
    d.poli([(0.6, 0.0), (L78 - 0.6, 0.0), (L78, 0.6), (L78, 5.5), (L78 - 0.5, 5.5), (L78 - 0.5, 6.4), (0.5, 6.4),
            (0.5, 5.5), (0.0, 5.5), (0.0, 0.6)], 'url(#niquel)', '#7b8088', 0.1)
    d.circ(L78 / 2, 2.8, 1.85, '#24262a', stroke='#6f757c', sw=0.15)
    d.circ(L78 / 2, 2.8, 1.85, None, '#e9ecef', 0.12)
    # as pernas: o ombro largo junto do corpo e a perna fina ate o furo
    for x in XS_78:
        d.ret_c(x, 16.3, 1.5, 2.2, 'url(#metal-h)')
        d.ret(x - 0.4, 17.3, 0.8, Y_PERNA - 17.3 + 0.5, 'url(#metal-h)')
    # o corpo de epoxi, com a marcacao
    d.ret(0.0, 6.2, L78, 9.0, '#1d1e22', rx=0.25, stroke='#0a0a0b', sw=0.1)
    d.ret(0.0, 6.2, L78, 9.0, 'url(#luz)', rx=0.25)
    d.texto(L78 / 2, 9.3, f'L{modelo}CV', 1.35, '#d9dce0', alinhar='center', peso='bold')
    d.texto(L78 / 2, 11.4, 'GK24Q', 1.0, '#a9aeb5', alinhar='center', peso='normal')
    d.texto(L78 / 2, 13.2, 'CHN 412', 0.85, '#8e949c', alinhar='center', peso='normal')
    d.circ(1.4, 14.0, 0.35, '#2c2e33')
    for x in XS_78:
        d.circ(x, Y_PERNA, 0.62, pa.OURO)
        furo(d, x, Y_PERNA, 0.3)
    return svg_de(d, L78, A78)


for modelo in MODELOS_78:
    gravar('regulador-7805' if modelo == '7805' else f'regulador-7805-{modelo}', to220(modelo))
guardar('regulador-7805', L78, A78, [
    ('IN', XS_78[0], Y_PERNA, ['power'], 'Entrada: o + da bateria ou da fonte. Precisa de ~2 V a mais que a saida (o 7805 pede 7 V para dar 5 V); maximo 35 V'),
    ('GND', XS_78[1], Y_PERNA, ['power'], 'GND: o - da bateria, o GND da placa e o da carga, todos aqui'),
    ('OUT', XS_78[2], Y_PERNA, ['power'], 'Saida regulada (5 V no 7805, 12 V no 7812...), ate 1,5 A. O que sobra da entrada vira calor'),
])


# =====================================================================
#  AMS1117 (o modulo de 25 x 11 mm, de 3,3 V ou de 5 V)
# =====================================================================
LA, AA = 94 / A, 42 / A                                 # 24,9 x 11,1 mm: a caixa inteira (94 x 42 px)
X_ENT, X_SAI, YS_A = 1.55, LA - 1.55, (AA / 2 - 1.27, AA / 2 + 1.27)
LED_A = (7.6, 9.3)


def ams1117(volts):
    d = Desenho(T(A, 0, 0, A, 0, 0))
    placa(d, LA, AA, 0.6)
    # a serigrafia
    d.texto(3.3, 1.2, 'VIN+', 0.8, SERIGRAFIA, alinhar='center-left', peso='normal')
    d.texto(3.3, AA - 1.0, 'GND-', 0.8, SERIGRAFIA, alinhar='center-left', peso='normal')
    d.texto(LA - 3.3, 1.2, 'VOUT+', 0.8, SERIGRAFIA, alinhar='center-right', peso='normal')
    d.texto(LA - 3.3, AA - 1.0, 'GND-', 0.8, SERIGRAFIA, alinhar='center-right', peso='normal')
    d.texto(LED_A[0] + 1.4, LED_A[1], 'LED', 0.75, SERIGRAFIA, alinhar='center-left', peso='normal')
    for x0, y0, w, h in ((LED_A[0] - 1.25, LED_A[1] - 0.85, 2.5, 1.7), (6.35, 2.8, 1.9, 3.0), (16.75, 1.9, 1.9, 3.0),
                         (18.0, 5.25, 3.0, 1.9), (15.4, 7.85, 2.4, 1.7)):
        d.ret(x0, y0, w, h, None, rx=0.2, stroke=SERIGRAFIA, sw=0.12)
    # as duas barras de 2 pinos
    for x in (X_ENT, X_SAI):
        for y in YS_A:
            pino_header(d, x, y)
    # o SOT-223: as tres pernas em cima, a aba larga embaixo
    cx = LA / 2
    for dx in (-2.3, 0.0, 2.3):
        d.ret_c(cx + dx, 2.75, 0.7, 1.5, 'url(#metal-h)')
    d.ret_c(cx, 7.55, 3.0, 1.6, 'url(#metal-h)')
    pa.ci_em(d, cx, 5.2, 6.5, 3.5, [(f'1117-{volts}', 0.75), ('AMS1117', 0.85)])
    # os capacitores, o resistor e o LED de energia
    dois(d, 7.3, 4.3, True, CAPACITOR)
    dois(d, 17.7, 3.4, True, CAPACITOR)
    dois(d, 19.5, 6.2, False, CAPACITOR)
    dois(d, 16.6, 8.7, False, RESISTOR, 1.6, 0.85, '102')
    d.ret_c(LED_A[0], LED_A[1], 1.6, 0.85, '#f4f2ec', rx=0.08)
    d.ret_c(LED_A[0], LED_A[1], 0.9, 0.55, '#e3dfe6', rx=0.1)
    return svg_de(d, LA, AA)


gravar('ams1117', ams1117('3.3'))
gravar('ams1117-5v0', ams1117('5.0'))
guardar('ams1117', LA, AA, [
    ('VIN', X_ENT, YS_A[0], ['power'], 'Entrada: 4,5 a 12 V (no maximo 15 V). Precisa de ~1,1 V a mais que a saida'),
    ('GND', X_ENT, YS_A[1], ['power'], 'GND da entrada (o mesmo da saida)'),
    ('VOUT', X_SAI, YS_A[0], ['power'], 'Saida regulada: 3,3 V (ou 5 V no AMS1117-5.0), ate ~0,8 A'),
    ('GND.2', X_SAI, YS_A[1], ['power'], 'GND da saida (o mesmo da entrada)'),
])
print(f'  LED do AMS1117 em ({LED_A[0] * A:.1f}, {LED_A[1] * A:.1f}) px')


# =====================================================================
#  LM2596 (step-down), medido na foto
# =====================================================================
LL, AL = 43.0, 21.0
# as ilhas nos quatro cantos (o furo a 1,4 mm das bordas), com a legenda por dentro
PINOS_L = [('IN+', 1.4, 1.4), ('IN-', 1.4, AL - 1.4), ('OUT+', LL - 1.4, 1.4), ('OUT-', LL - 1.4, AL - 1.4)]
d = Desenho(T(A, 0, 0, A, 0, 0))
placa(d, LL, AL)
for x, y in ((6.6, 2.2), (36.8, 18.6)):                       # os furos de fixacao
    d.circ(x, y, 1.7, 'url(#ouro)', stroke='#8a6a22', sw=0.1)
    furo(d, x, y, 1.25)
for n, x, y in PINOS_L:
    d.ret_c(x, y, 2.7, 2.7, ESTANHO, rx=0.5)
    furo(d, x, y, 0.55)
    esquerda = x < LL / 2
    d.texto(0.4 if esquerda else LL - 0.4, 4.0 if y < AL / 2 else AL - 4.0, n, 1.0, SERIGRAFIA,
            alinhar='center-left' if esquerda else 'center-right', peso='normal')
# o LM2596S (TO-263-5): a aba atras, as cinco pernas para baixo (passo de 1,7 mm)
d.ret_c(14.75, 1.4, 8.0, 1.6, 'url(#metal)')
for k in range(5):
    x = 14.75 + (k - 2) * 1.7
    d.ret(x - 0.4, 10.6, 0.8, 4.4, 'url(#metal-h)')
    d.ret_c(x, 15.4, 1.1, 1.6, ESTANHO, rx=0.1)
pa.ci_em(d, 14.75, 6.3, 10.1, 8.6, [('ADJ', 0.85), ('LM2596S', 1.05)])
# os SMD miudos: os capacitores de desacoplamento e o resistor do ajuste
dois(d, 6.6, 16.6, True, CAPACITOR)
dois(d, 4.2, 15.4, False, CAPACITOR, 1.6, 0.85)
dois(d, 22.1, 1.6, True, CAPACITOR, 1.6, 0.85)
dois(d, 35.9, 2.3, False, RESISTOR, 2.0, 1.25, '102')
dois(d, 38.8, 15.5, False, CAPACITOR, 1.6, 0.85)
sma(d, 12.6, 18.8, 'SS34', banda='dir', larg=4.2, alt=3.1, passo=3.0)
# o indutor de 33 uH, quase na borda de baixo
indutor(d, 26.15, 14.3, 12.1, 12.0, 5.1, '330')
# os eletroliticos SMD (100 uF na entrada, 220 uF na saida) e o trimpot em pe na borda de cima
eletrolitico_smd(d, 4.3, 10.2, 4.0, ['100', '50V', 'RVT'])
eletrolitico_smd(d, 38.7, 10.2, 4.0, ['220', '35V', 'RVT'])
trimpot_em_pe(d, 23.8, 0.3, 9.5, 4.8, 25.1)
gravar('lm2596', svg_de(d, LL, AL))
guardar('lm2596', LL, AL, [
    ('IN+', PINOS_L[0][1], PINOS_L[0][2], ['power'], 'Entrada +: 4,5 a 40 V (precisa de ~1,5 V a mais que a saida)'),
    ('IN-', PINOS_L[1][1], PINOS_L[1][2], ['power'], 'Entrada - (GND): e o mesmo GND da saida'),
    ('OUT+', PINOS_L[2][1], PINOS_L[2][2], ['power'], 'Saida +: a tensao ajustada no trimpot (1,25 a 35 V; 5 V de fabrica aqui), ate 3 A'),
    ('OUT-', PINOS_L[3][1], PINOS_L[3][2], ['power'], 'Saida - (GND): ligue no GND da placa'),
])


# =====================================================================
#  MT3608 (step-up), pelo desenho de cotas
# =====================================================================
LM, AM = 37.2, 17.3
PINOS_M = [('VIN+', LM - 2.3, 5.2), ('VIN-', LM - 2.3, 13.0), ('VOUT+', 2.3, 5.2), ('VOUT-', 2.3, 13.0)]
SOBRA_M = round(0.8 * A + 0.6, 1)                              # o parafuso do trimpot, para fora da borda
d = Desenho(T(A, 0, 0, A, 0, SOBRA_M))
placa(d, LM, AM, 0.9)
for n, x, y in PINOS_M:
    esquerda = x < LM / 2
    d.ret_c(x + (1.0 if esquerda else -1.0), y, 4.9, 2.6, ESTANHO, rx=0.2)
    furo(d, x, y, 0.55)
    d.texto(0.5 if esquerda else LM - 0.5, y + (-2.4 if y < AM / 2 else 2.4), n, 0.95, SERIGRAFIA,
            alinhar='center-left' if esquerda else 'center-right', peso='normal')
sma(d, 13.0, 2.5, 'SS34', banda='esq')
indutor(d, 24.4, 6.2, 10.0, 9.6, 4.0, '220')
for s in (-1, 1):                                               # os terminais do indutor, dos lados
    d.ret_c(24.4 + s * 5.2, 8.6, 0.9, 3.2, ESTANHO, rx=0.15)
for dx in (-0.95, 0.0, 0.95):                                   # o MT3608 (SOT-23-6)
    d.ret_c(25.2 + dx, 12.95, 0.45, 0.7, 'url(#metal-h)')
    d.ret_c(25.2 + dx, 14.95, 0.45, 0.7, 'url(#metal-h)')
pa.ci_em(d, 25.2, 13.95, 2.9, 1.6, [('B628', 0.55)])
dois(d, 21.4, 13.8, True, CAPACITOR, 3.2, 1.6)
dois(d, 29.5, 13.8, True, CAPACITOR, 3.2, 1.6)
dois(d, 18.3, 14.7, True, RESISTOR, 1.6, 0.85, '222')
trimpot_deitado(d, 7.0, 5.9, 9.8, (8.6, 16.6))
gravar('mt3608', svg_de(d, LM, AM, 0, SOBRA_M))
guardar('mt3608', LM, AM, [
    ('VIN+', PINOS_M[0][1], PINOS_M[0][2], ['power'], 'Entrada +: 2 a 24 V (uma celula de litio, duas pilhas...)'),
    ('VIN-', PINOS_M[1][1], PINOS_M[1][2], ['power'], 'Entrada - (GND): e o mesmo GND da saida'),
    ('VOUT+', PINOS_M[2][1], PINOS_M[2][2], ['power'], 'Saida +: a tensao ajustada no trimpot (ate 28 V; 5 V de fabrica aqui). So SOBE: com a entrada acima, sai a entrada'),
    ('VOUT-', PINOS_M[3][1], PINOS_M[3][2], ['power'], 'Saida - (GND): ligue no GND da placa'),
])


cam = pa.BASE + 'app/assets/pecas.json'
lista = json.load(io.open(cam, encoding='utf-8'))
novos = {p['id'] for p in pecas}
lista = [p for p in lista if p['id'] not in novos] + pecas
lista.sort(key=lambda p: p['id'])
io.open(cam, 'w', encoding='utf-8', newline='').write(json.dumps(lista, ensure_ascii=False, indent=1))
print(f'catalogo: {len(lista)} pecas')
