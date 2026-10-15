"""python ferramentas/extrair-svg/pecas-outros.py

Os "Outros" desenhados a mao (Alpha 25), no estilo das placas (as primitivas
e a paleta de placas-arduino.py), em escala real (96 px por polegada):

  - ir-receiver.svg: o modulo receptor infravermelho (o KY-022): a placa
    preta, o receptor VS1838B com a lente, o LED vermelho que pisca quando
    chega sinal, o resistor e a barra de 3 pinos em angulo;
  - ir-remote.svg: o controle de membrana de 20 teclas (40 x 84 mm), as
    teclas no mesmo lugar de antes (o editor sabe onde cada uma esta), o LED
    infravermelho na ponta e a gaveta da pilha CR2025;
  - pendrive-usb.svg: a plaquinha da tomada USB-A com os 4 pinos e o
    pendrive espetado nela.

O DS1307 e o microSD saem dos arquivos de projeto (placas-arduino.py).
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

Desenho, T, mover, rodar, f = pa.Desenho, pa.T, pa.mover, pa.rodar, pa.f
SERIGRAFIA, ESTANHO, OURO, PRETO_CI, MARCA_CI = pa.SERIGRAFIA, pa.ESTANHO, pa.OURO, pa.PRETO_CI, pa.MARCA_CI
BASE = pa.BASE
A = 96 / 25.4
PECAS = {p['id']: p for p in json.load(io.open(BASE + 'app/assets/pecas.json', encoding='utf-8'))}

DEFS = pa.DEFS + '''  <radialGradient id="cupula" cx="0.4" cy="0.35" r="0.65"><stop offset="0" stop-color="#5a5c62"/><stop offset="0.55" stop-color="#202125"/><stop offset="1" stop-color="#0b0b0d"/></radialGradient>
  <radialGradient id="tecla" cx="0.42" cy="0.36" r="0.68"><stop offset="0" stop-color="#ffffff"/><stop offset="0.7" stop-color="#eeeeec"/><stop offset="1" stop-color="#cfcfcc"/></radialGradient>
  <radialGradient id="tecla-preta" cx="0.42" cy="0.36" r="0.68"><stop offset="0" stop-color="#4c4d52"/><stop offset="0.6" stop-color="#1d1e21"/><stop offset="1" stop-color="#0c0c0e"/></radialGradient>
  <linearGradient id="corpo-controle" x1="0" y1="0" x2="1" y2="0"><stop offset="0" stop-color="#e2e2df"/><stop offset="0.12" stop-color="#fbfbf9"/><stop offset="0.85" stop-color="#f4f4f1"/><stop offset="1" stop-color="#d6d6d2"/></linearGradient>
  <radialGradient id="lente-ir" cx="0.4" cy="0.35" r="0.7"><stop offset="0" stop-color="#7a3a4a"/><stop offset="0.6" stop-color="#2a0f16"/><stop offset="1" stop-color="#120608"/></radialGradient>
  <linearGradient id="plastico-pen" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#3b3d42"/><stop offset="0.35" stop-color="#26282c"/><stop offset="1" stop-color="#121315"/></linearGradient>
'''


def documento(nome, partes, tipo=None):
    L, H = PECAS[tipo or nome]['largura'], PECAS[tipo or nome]['altura']
    svg = (f'<svg xmlns="http://www.w3.org/2000/svg" version="1.1" width="{f(L)}px" height="{f(H)}px" '
           f'viewBox="0 0 {f(L)} {f(H)}">\n<defs>\n{DEFS}</defs>\n' + ''.join(partes) + '</svg>\n')
    io.open(BASE + f'app/assets/{nome}.svg', 'w', encoding='utf-8', newline='').write(svg)
    print(f'  {nome}.svg: {len(svg) // 1024 + 1} KB')


def pinos(nome):
    return {q['nome']: (q['x'] / A, q['y'] / A) for q in PECAS[nome]['pinos']}


# =====================================================================
#  o receptor infravermelho (KY-022)
# =====================================================================
IR_LED = None


def gerar_receptor():
    global IR_LED
    p = pinos('ir-receiver')
    xs = [p[n][0] for n in ('GND', 'VCC', 'DAT')]
    yp = p['GND'][1]
    cx = sum(xs) / 3
    d = Desenho(T(A, 0, 0, A, 0, 0))
    L, y0, y1 = 15.0, 0.4, 15.6                  # a placa
    x0 = cx - L / 2
    # a barra em angulo: o plastico na borda e os pinos saindo
    for x in xs:
        d.ret(x - 0.32, y1 - 0.4, 0.64, yp - y1 + 0.75, 'url(#metal-h)')
    d.ret(x0, y0, L, y1 - y0, '#16171a', rx=0.6, stroke='#0a0a0b', sw=0.1)
    d.ret(x0, y0, L, y1 - y0, 'url(#luz)', rx=0.6)
    d.ret_c(cx, y1 + 1.25, 7.6, 2.5, 'url(#plastico)', rx=0.2, stroke='#060607', sw=0.08)
    for x in xs:
        d.circ(x, y1 - 1.4, 0.8, OURO)
        d.circ(x, y1 - 1.4, 0.45, ESTANHO)
    for x, t in zip(xs, ('−', '+', 'S')):
        d.texto(x, y1 - 3.2, t, 1.0, SERIGRAFIA, ancora='middle', alinhar='center')
    # os furos de fixacao
    d.circ(x0 + 1.6, y0 + 6.2, 1.2, '#0a0a0b', stroke=OURO, sw=0.35)
    # o receptor VS1838B: as pernas dobradas, o corpo preto e a lente
    for k in (-1, 0, 1):
        d.ret(cx + k * 1.27 - 0.22, 7.2, 0.44, 2.2, 'url(#metal-h)')
        d.circ(cx + k * 1.27, 9.3, 0.6, OURO)
    d.ret_c(cx, 3.6, 6.8, 6.6, '#000000', rx=0.8, op=0.35)                 # a sombra na placa
    d.ret_c(cx, 3.4, 6.4, 6.4, '#26272b', rx=0.7, stroke='#4a4c52', sw=0.1)
    d.ret_c(cx, 3.4, 6.4, 6.4, 'url(#luz)', rx=0.7)
    d.circ(cx, 3.4, 2.35, '#0e0f11')
    d.circ(cx, 3.4, 2.15, 'url(#cupula)', stroke='#5a5c62', sw=0.06)
    d.caminho([('M', cx - 1.3, 2.4), ('A', 1.7, 0, 1, cx + 0.2, 1.6)], None, '#ffffff', 0.22, so=0.55, cap='round')
    # o LED de sinal (0805, vermelho) e o resistor
    lx, ly = x0 + L - 3.1, 11.2
    d.ret_c(lx, ly, 1.25, 2.0, '#f3f1ea', rx=0.1)
    d.ret_c(lx, ly - 0.75, 1.25, 0.45, 'url(#metal)')
    d.ret_c(lx, ly + 0.75, 1.25, 0.45, 'url(#metal)')
    d.ret_c(lx, ly, 0.7, 0.7, '#ff4a3d', rx=0.12, op=0.7)
    d.ret_c(lx + 0.02, ly - 3.0, 1.25, 2.0, '#1c1c1e', rx=0.1)
    d.ret_c(lx + 0.02, ly - 3.75, 1.25, 0.45, 'url(#metal)')
    d.ret_c(lx + 0.02, ly - 2.25, 1.25, 0.45, 'url(#metal)')
    d.texto(lx + 0.02, ly - 3.0, '102', 0.42, '#e8e8e8', ang=90, ancora='middle', alinhar='center', peso='normal')
    IR_LED = (lx * A, ly * A)
    documento('ir-receiver', d.partes)


# =====================================================================
#  o controle remoto
# =====================================================================
# O centro (px) e o nome de cada tecla: os mesmos de EditorCircuito.BotoesDoControle.
TECLAS = [
    (29.6, 37.9, 'power'), (121.4, 37.9, 'menu'),
    (29.6, 75.2, 'test'), (75.5, 75.2, '+'), (121.4, 75.2, 'back'),
    (29.6, 113, 'prev'), (75.5, 113, 'play'), (121.4, 113, 'next'),
    (29.6, 152, '0'), (75.5, 152, '-'), (121.4, 152, 'c'),
    (29.6, 190, '1'), (75.5, 190, '2'), (121.4, 190, '3'),
    (29.6, 228, '4'), (75.5, 228, '5'), (121.4, 228, '6'),
    (29.6, 266, '7'), (75.5, 266, '8'), (121.4, 266, '9'),
]
PRETAS = {'power', '+', 'prev', 'next', '-'}


def icone(d, cx, cy, nome, cor):
    """O simbolo de cada tecla, em mm, no centro (cx, cy)."""
    if nome == 'power':
        d.caminho([('M', cx - 1.15, cy - 1.0), ('A', 1.6, 1, 0, cx + 1.15, cy - 1.0)], None, cor, 0.42, cap='round')
        d.linha(cx, cy - 1.95, cx, cy - 0.3, cor, 0.42)
    elif nome in ('menu', 'test'):
        d.texto(cx, cy, nome.upper(), 1.05, '#d8232a', ancora='middle', alinhar='center')
    elif nome == '+':
        d.linha(cx - 1.4, cy, cx + 1.4, cy, cor, 0.38)
        d.linha(cx, cy - 1.4, cx, cy + 1.4, cor, 0.38)
    elif nome == '-':
        d.linha(cx - 1.5, cy, cx + 1.5, cy, cor, 0.38)
    elif nome == 'back':
        d.caminho([('M', cx + 1.2, cy + 1.2), ('A', 1.15, 0, 0, cx + 0.05, cy - 0.85), ('L', cx - 1.0, cy - 0.85)], None, cor, 0.38, cap='round')
        d.poli([(cx - 1.55, cy - 0.85), (cx - 0.75, cy - 1.45), (cx - 0.75, cy - 0.25)], cor)
    elif nome == 'play':
        d.poli([(cx - 0.75, cy - 1.05), (cx + 1.0, cy), (cx - 0.75, cy + 1.05)], cor)
    elif nome in ('prev', 'next'):
        s = -1 if nome == 'prev' else 1
        for k in (0, 1):
            x = cx + s * (-0.95 + k * 0.95)
            d.poli([(x - s * 0.5, cy - 0.85), (x + s * 0.5, cy), (x - s * 0.5, cy + 0.85)], cor)
        d.linha(cx + s * 1.15, cy - 0.85, cx + s * 1.15, cy + 0.85, cor, 0.28)
    else:
        d.texto(cx, cy, nome.upper(), 1.6, cor, ancora='middle', alinhar='center')


def gerar_controle():
    L, H = PECAS['ir-remote']['largura'] / A, PECAS['ir-remote']['altura'] / A
    d = Desenho(T(A, 0, 0, A, 0, 0))
    # o corpo de plastico branco, com a borda e a gaveta da pilha embaixo
    d.ret(0.35, 0.35, L - 0.7, H - 0.7, 'url(#corpo-controle)', rx=5.2, stroke='#b9b9b5', sw=0.12)
    d.ret(0.9, 0.9, L - 1.8, H - 1.8, None, rx=4.6, stroke='#ffffff', sw=0.1, op=0.6)
    gy = H - 6.0
    d.ret(L / 2 - 9.5, gy, 19.0, 4.6, None, rx=1.0, stroke='#c8c8c4', sw=0.12)
    d.texto(L / 2 - 3.0, gy + 2.3, 'OPEN', 0.85, '#9a9a96', ancora='middle', alinhar='center')
    d.poli([(L / 2 + 1.4, gy + 1.9), (L / 2 + 4.0, gy + 1.9), (L / 2 + 4.0, gy + 1.2), (L / 2 + 5.4, gy + 2.3),
            (L / 2 + 4.0, gy + 3.4), (L / 2 + 4.0, gy + 2.7), (L / 2 + 1.4, gy + 2.7)], '#a8a8a4')
    # o LED infravermelho na ponta de cima
    d.ret_c(L / 2, 1.4, 5.0, 2.2, 'url(#lente-ir)', rx=1.0)
    d.ret_c(L / 2, 1.0, 3.2, 0.5, '#ffffff', rx=0.25, op=0.18)
    # as teclas de membrana
    r = 16.3 / A
    for x, y, nome in TECLAS:
        cx, cy = x / A, y / A
        preta = nome in PRETAS
        d.circ(cx, cy + 0.18, r + 0.12, '#000000', op=0.12)
        d.circ(cx, cy, r, 'url(#tecla-preta)' if preta else 'url(#tecla)', stroke='#2a2b2e' if preta else '#55565a', sw=0.18)
        d.caminho([('M', cx - r * 0.62, cy - r * 0.45), ('A', r * 0.8, 0, 1, cx + r * 0.05, cy - r * 0.78)], None,
                  '#ffffff', 0.22, so=0.35 if preta else 0.8, cap='round')
        icone(d, cx, cy, nome, '#ffffff' if preta else '#1d1e21')
    documento('ir-remote', d.partes)


# =====================================================================
#  o pendrive na tomada USB-A
# =====================================================================
PEN_LED = None


def gerar_pendrive():
    """Tres desenhos, como antes (o editor anima o pendrive): o inteiro (a
    paleta), o "-corpo" (a plaquinha com a tomada) e o "-movel" (o pendrive,
    que desliza 9 mm para fora quando e tirado)."""
    global PEN_LED
    p = pinos('pendrive-usb')
    nomes = ['VBUS', 'D-', 'D+', 'GND']
    xp = p['VBUS'][0]
    ys = [p[n][1] for n in nomes]
    cy = (ys[0] + ys[-1]) / 2
    L, H = PECAS['pendrive-usb']['largura'] / A, PECAS['pendrive-usb']['altura'] / A
    tx0, tx1, tw = 8.2, 21.6, 13.2

    corpo = Desenho(T(A, 0, 0, A, 0, 0))
    d = corpo
    # a plaquinha da tomada (azul), com os 4 furos e os nomes
    px0, px1 = 0.4, 16.6
    d.ret(px0, cy - 7.0, px1 - px0, 14.0, '#1b5fae', rx=0.8, stroke='#0f3f78', sw=0.12)
    d.ret(px0, cy - 7.0, px1 - px0, 14.0, 'url(#luz)', rx=0.8)
    for y, n in zip(ys, nomes):
        d.circ(xp, y, 0.82, OURO)
        d.circ(xp, y, 0.45, '#0c1016')
        d.texto(xp + 1.4, y, n, 0.8, SERIGRAFIA, alinhar='left')
    # a tomada USB-A: a carcaca de aco, as patinhas soldadas e a boca a direita
    for x in (tx0 + 2.0, tx1 - 3.5):
        for sgn in (-1, 1):
            d.ret_c(x, cy + sgn * (tw / 2 + 0.25), 1.2, 0.8, ESTANHO, rx=0.1)
    d.ret(tx0, cy - tw / 2, tx1 - tx0, tw, 'url(#metal)', rx=0.35, stroke='#6f767e', sw=0.08)
    d.linha(tx0 + 0.6, cy - tw / 2 + 1.1, tx1 - 0.6, cy - tw / 2 + 1.1, '#9aa1a8', 0.12)
    d.linha(tx0 + 0.6, cy + tw / 2 - 1.1, tx1 - 0.6, cy + tw / 2 - 1.1, '#9aa1a8', 0.12)
    d.ret(tx1 - 0.7, cy - tw / 2 + 0.6, 0.6, tw - 1.2, '#2a2c30')                 # a boca
    d.ret(tx1 - 0.7, cy - 2.6, 0.6, 5.2, '#e9e5d8')                                 # a lingueta de plastico la dentro

    movel = Desenho(T(A, 0, 0, A, 0, 0))
    d = movel
    # o pendrive: o plugue de metal (dentro da tomada quando encaixado)...
    bx0, bx1, bh = tx1 - 0.6, L - 0.3, 12.2
    d.ret(bx0 - 12.0, cy - 6.0, 12.4, 12.0, 'url(#metal)', rx=0.3, stroke='#6f767e', sw=0.08)
    for k in (-1, 1):
        d.ret_c(bx0 - 4.6, cy + k * 2.2, 1.5, 1.5, '#3a3d42', rx=0.15)
    # ...e a capa de plastico preto, o furo do chaveiro, a etiqueta e o LED
    d.ret(bx0, cy - bh / 2, bx1 - bx0, bh, 'url(#plastico-pen)', rx=1.4, stroke='#060607', sw=0.1)
    d.ret(bx0 + 0.3, cy - bh / 2 + 0.3, 1.0, bh - 0.6, '#3d3f44', rx=0.3)
    d.ret(bx0 + 2.6, cy - 3.3, 7.4, 6.6, '#1d1e22', rx=0.6, stroke='#3a3c41', sw=0.08)
    d.texto(bx0 + 6.3, cy, '16 GB', 1.3, '#e8e8e6', ancora='middle', alinhar='center')
    d.circ(bx1 - 1.9, cy, 1.0, '#08090a', stroke='#3a3c41', sw=0.1)
    PEN_LED = ((bx1 - 4.1) * A, (cy + 4.2) * A)
    d.circ(bx1 - 4.1, cy + 4.2, 0.45, '#3b8cff', stroke='#16233a', sw=0.06)

    documento('pendrive-usb', corpo.partes + movel.partes)
    io.open(BASE + 'app/assets/pendrive-usb-corpo.svg', 'w', encoding='utf-8', newline='').write(
        io.open(BASE + 'app/assets/pendrive-usb.svg', encoding='utf-8').read().replace(''.join(movel.partes), ''))
    documento('pendrive-usb-movel', movel.partes, 'pendrive-usb')


if __name__ == '__main__':
    gerar_receptor()
    gerar_controle()
    gerar_pendrive()
    print(f'  LED do receptor: ({IR_LED[0]:.1f}, {IR_LED[1]:.1f}) px; LED do pendrive: ({PEN_LED[0]:.1f}, {PEN_LED[1]:.1f}) px')
