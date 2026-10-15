"""python ferramentas/extrair-svg/placas-esp32.py [placa ...]

As DevKits da Espressif redesenhadas (Alpha 25), no mesmo estilo das placas
do Arduino (placas-arduino.py: as mesmas primitivas, a mesma paleta, as
mesmas barras de pinos). O lugar e o tamanho de cada peca saem do layout
oficial de cada placa (esp32-cad/*.json, tirados do PDF pelo
esp32_layout.py): o centro de cada ilha, os nomes dos pinos e os textos da
serigrafia. O desenho de cada peca e nosso; nenhuma trilha e copiada.

  - ESP32-S3-DevKitC-1 (v1.0: RGB no IO48, o LED_BUILTIN do core);
  - ESP32-S2-DevKitM-1, ESP32-C3-DevKitM-1, ESP32-C6-DevKitC-1 e
    ESP32-H2-DevKitM-1.

A placa fica de pe, com a antena do modulo em cima (para fora da placa, como
na de verdade) e o USB embaixo. A caixa da peca e o desenho inteiro (placa e
antena); os pinos ficam no furo de verdade, na escala real (96 px por
polegada), e sao gravados no pecas.json com os nomes da serigrafia (os de
energia numerados: GND, GND.2, ...; o RST da serigrafia e o pino EN).

O logotipo da Espressif nao e desenhado (marca registrada); o nome do modulo
fica, como na blindagem.
"""
import importlib.util
import io
import json
import math
import os
import random
import re
import sys

AQUI = os.path.dirname(os.path.abspath(__file__))
_spec = importlib.util.spec_from_file_location('placas_arduino', os.path.join(AQUI, 'placas-arduino.py'))
pa = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(pa)

Desenho, T, mover, rodar = pa.Desenho, pa.T, pa.mover, pa.rodar
SERIGRAFIA, OURO, ESTANHO, PRETO_CI, MARCA_CI = pa.SERIGRAFIA, pa.OURO, pa.ESTANHO, pa.PRETO_CI, pa.MARCA_CI
BASE = pa.BASE
CAD = os.path.join(AQUI, 'esp32-cad')
A = 96 / 25.4          # px por mm

# A mascara preta das DevKits (fosca, um pouco azulada como nas fotos).
PRETA = '#1d1f23'
PRETA_BORDA = '#121316'
SUBSTRATO = '#17181b'   # a placa do modulo, em volta da blindagem e na antena

DEFS_EXTRA = '''  <linearGradient id="blindagem" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#eef0f2"/><stop offset="0.45" stop-color="#cfd3d7"/><stop offset="1" stop-color="#a9aeb4"/></linearGradient>
  <radialGradient id="botao" cx="0.4" cy="0.35" r="0.7"><stop offset="0" stop-color="#4a4c52"/><stop offset="0.7" stop-color="#1c1d20"/><stop offset="1" stop-color="#0d0d0f"/></radialGradient>
  <radialGradient id="lente" cx="0.45" cy="0.4" r="0.6"><stop offset="0" stop-color="#ffffff"/><stop offset="0.7" stop-color="#f1ede2"/><stop offset="1" stop-color="#d9d3c4"/></radialGradient>
'''


# =====================================================================
#  as pecas (em mm, y para cima, no lugar da placa)
# =====================================================================
def eixo(p1, p2):
    (x1, y1), (x2, y2) = p1, p2
    return (x1 + x2) / 2, (y1 + y2) / 2, math.degrees(math.atan2(y2 - y1, x2 - x1)), math.hypot(x2 - x1, y2 - y1)


def dois(d, ilhas, comp, larg, corpo, terminal=True, frac=0.22, banda=None, marca=None):
    """Resistor, capacitor, diodo: o corpo no eixo das duas ilhas, com o
    estanho das ilhas aparecendo nas pontas (como no gerador do Arduino)."""
    cx, cy, ang, passo = eixo(ilhas['1'], ilhas['2'])
    d.entrar(mover(cx, cy) * rodar(ang))
    ilha = max(passo - comp * 0.55, larg * 0.9)
    for s in (-1, 1):
        d.ret_c(s * passo / 2, 0, max(ilha * 0.62, 0.35), larg * 1.12, ESTANHO, rx=0.05)
    d.ret_c(0, 0, comp, larg, corpo, rx=larg * 0.08)
    d.ret_c(0, 0, comp, larg, 'url(#luz)', rx=larg * 0.08)
    if terminal:
        t = comp * frac
        d.ret(-comp / 2, -larg / 2, t, larg, 'url(#metal)')
        d.ret(comp / 2 - t, -larg / 2, t, larg, 'url(#metal)')
    if banda is not None:
        d.ret_c(banda * (comp / 2 - comp * 0.18), 0, comp * 0.12, larg, '#9ea3a8')
    if marca:
        d.texto(0, 0, marca, larg * 0.42, '#e8e8e8', ancora='middle', alinhar='center', peso='normal')
    d.sair()


def caixa(ilhas):
    xs = [p[0] for p in ilhas.values()]
    ys = [p[1] for p in ilhas.values()]
    return (min(xs) + max(xs)) / 2, (min(ys) + max(ys)) / 2, max(xs) - min(xs), max(ys) - min(ys)


def perna(d, x0, y0, x1, y1, larg):
    """Uma perna de estanho de (x0, y0) ate (x1, y1)."""
    if abs(x1 - x0) > abs(y1 - y0):
        d.ret(min(x0, x1), y0 - larg / 2, abs(x1 - x0), larg, 'url(#metal)')
    else:
        d.ret(x0 - larg / 2, min(y0, y1), larg, abs(y1 - y0), 'url(#metal-h)')


def sot23(d, ilhas, marca='J3Y'):
    """SOT-23: o corpo de 2,9 x 1,3 entre as duas ilhas de um lado e a do outro."""
    p1, p2, p3 = ilhas['1'], ilhas['2'], ilhas['3']
    cx, cy = (p1[0] + p2[0]) / 2, (p1[1] + p2[1]) / 2
    ang = math.degrees(math.atan2(p2[1] - p1[1], p2[0] - p1[0]))
    lado = 1 if (p3[0] - cx) * -math.sin(math.radians(ang)) + (p3[1] - cy) * math.cos(math.radians(ang)) > 0 else -1
    meio = math.hypot(p3[0] - cx, p3[1] - cy) / 2
    d.entrar(mover(cx, cy) * rodar(ang) * mover(0, lado * meio))
    for x, y in ((-0.95, -lado * 1.15), (0.95, -lado * 1.15), (0, lado * 1.15)):
        d.ret_c(x, y, 0.75, 0.9, ESTANHO, rx=0.05)
        perna(d, x, 0, x, y * 0.85, 0.42)
    d.ret_c(0, 0, 2.9, 1.3, PRETO_CI, rx=0.1)
    d.ret_c(0, 0, 2.9, 1.3, 'url(#luz)', rx=0.1)
    d.texto(0, 0, marca, 0.55, MARCA_CI, ancora='middle', alinhar='center', peso='normal')
    d.sair()


def sot223(d, ilhas, marca):
    """SOT-223: tres pernas de um lado, a aba larga do outro."""
    p1, p3, p4 = ilhas['1'], ilhas['3'], ilhas['4']
    lx, ly = (p1[0] + p3[0]) / 2, (p1[1] + p3[1]) / 2          # o meio das tres pernas
    cx, cy = (lx + p4[0]) / 2, (ly + p4[1]) / 2
    ang = math.degrees(math.atan2(p4[1] - ly, p4[0] - lx))       # das pernas para a aba
    d.entrar(mover(cx, cy) * rodar(ang))
    for k in (-1, 0, 1):
        d.ret_c(-3.15, k * 2.3, 2.0, 1.2, ESTANHO, rx=0.05)
        perna(d, -1.75, k * 2.3, -3.6, k * 2.3, 0.7)
    d.ret_c(3.15, 0, 2.0, 3.6, ESTANHO, rx=0.05)
    perna(d, 1.75, 0, 3.6, 0, 3.0)
    d.ret_c(0, 0, 3.5, 6.5, PRETO_CI, rx=0.15)
    d.ret_c(0, 0, 3.5, 6.5, 'url(#luz)', rx=0.15)
    d.texto(0, 0, marca, 0.7, MARCA_CI, ang=90, ancora='middle', alinhar='center', peso='normal')
    d.sair()


def qfn(d, ilhas, lado, linhas, n_lado=7):
    """QFN de 'lado' mm com as ilhas em volta (o estanho aparecendo) e a marcacao."""
    cx, cy, _, _ = caixa({k: v for k, v in ilhas.items() if int(k) <= 4 * n_lado})
    qfn_em(d, cx, cy, lado, linhas, n_lado)


def qfn_em(d, cx, cy, lado, linhas, n_lado=7):
    passo = 0.5
    for i in range(n_lado):
        o = (i - (n_lado - 1) / 2) * passo
        for x, y, w, h in ((cx + o, cy + lado / 2 + 0.15, 0.25, 0.5), (cx + o, cy - lado / 2 - 0.15, 0.25, 0.5),
                           (cx + lado / 2 + 0.15, cy + o, 0.5, 0.25), (cx - lado / 2 - 0.15, cy + o, 0.5, 0.25)):
            d.ret_c(x, y, w, h, ESTANHO)
    d.entrar(mover(cx, cy))
    pa.ci(d, lado, lado, linhas, pino1=(-lado / 2 + 0.55, lado / 2 - 0.55))
    d.sair()


def micro_usb(d, ilhas, base):
    """O micro-USB: a carcaca de metal com a boca na borda de baixo da placa."""
    cx, cy, w, h = caixa(ilhas)
    for p in ilhas.values():
        d.ret_c(p[0], p[1], 0.5, 1.2, ESTANHO)
    return micro_usb_em(d, cx, max(p[1] for p in ilhas.values()) - 0.3, base)


def micro_usb_em(d, cx, topo, base):
    y0 = base - 0.6
    d.ret(cx - 3.75, y0, 7.5, topo - y0, 'url(#metal-h)', rx=0.25, stroke='#5d646c', sw=0.08)
    d.ret(cx - 3.2, y0, 6.4, 0.55, '#5b6168')
    d.linha(cx - 3.1, topo - 0.9, cx + 3.1, topo - 0.9, '#8d949b', 0.1)
    for s in (-1, 1):
        d.ret_c(cx + s * 3.0, y0 + 2.2, 0.9, 1.6, None, rx=0.2, stroke='#8d949b', sw=0.1)
    return y0


def usb_c(d, ilhas, base):
    """O USB-C: a carcaca de metal com a boca oval na borda de baixo."""
    cx, cy, w, h = caixa(ilhas)
    ys = sorted(p[1] for p in ilhas.values())
    for p in ilhas.values():
        if p[1] > ys[-1] - 0.3:
            d.ret_c(p[0], p[1], 0.3, 1.1, ESTANHO)
    topo = ys[-1] - 0.6
    y0 = base - 0.6
    d.ret(cx - 4.47, y0, 8.94, topo - y0, 'url(#metal-h)', rx=0.6, stroke='#5d646c', sw=0.08)
    d.ret(cx - 4.1, y0 + 0.25, 8.2, 0.9, '#3e434a', rx=0.45)
    d.ret(cx - 3.3, y0 + 0.45, 6.6, 0.5, '#1d1f22', rx=0.25)
    d.linha(cx - 4.2, topo - 1.0, cx + 4.2, topo - 1.0, '#8d949b', 0.1)
    for s in (-1, 1):
        d.ret_c(cx + s * 4.6, cy - 1.2, 0.7, 1.8, ESTANHO, rx=0.15)
    return y0


def botao(d, ilhas):
    """A chave tatil de 4 x 3 mm: a base, a tampa de aco e o botao preto redondo."""
    cx, cy, w, h = caixa(ilhas)
    horizontal = w > h
    for p in ilhas.values():
        d.ret_c(p[0], p[1], 1.0 if horizontal else 0.9, 0.9 if horizontal else 1.0, ESTANHO, rx=0.05)
    bw, bh = (4.0, 3.0) if horizontal else (3.0, 4.0)
    d.ret_c(cx, cy, bw, bh, '#2a2b2f', rx=0.15)
    d.ret_c(cx, cy, bw - 0.3, bh - 0.3, 'url(#metal)', rx=0.2, stroke='#7c838b', sw=0.05)
    d.circ(cx, cy, 1.15, '#0b0b0d')
    d.circ(cx, cy, 1.0, 'url(#botao)')
    return cx, cy


def led(d, ilhas, cor):
    """O LED 0603: o corpo branco com o chip colorido no meio."""
    dois(d, ilhas, 1.6, 0.8, '#f3f1ea', terminal=True, frac=0.18)
    cx, cy, _, _ = eixo(ilhas['1'], ilhas['2'])
    d.ret_c(cx, cy, 0.45, 0.45, cor, rx=0.08, op=0.75)
    return cx, cy


def led_rgb(d, ilhas):
    """O LED RGB enderecavel (o SK68XX de 3,5 x 3,5 mm): o corpo branco, a
    lente redonda e os tres chips com o controlador."""
    cx, cy, w, h = caixa(ilhas)
    for p in ilhas.values():
        d.ret_c(p[0], p[1], 0.9 if w > h else 1.0, 1.0 if w > h else 0.9, ESTANHO, rx=0.05)
    d.ret_c(cx, cy, 3.3, 3.3, '#f4f2ec', rx=0.15, stroke='#c9c4b6', sw=0.06)
    d.circ(cx, cy, 1.35, 'url(#lente)', stroke='#d6cfbd', sw=0.05)
    for k, cor in enumerate(('#ff4a3d', '#5cd65a', '#4aa3ff')):
        d.ret_c(cx - 0.45 + k * 0.45, cy + 0.25, 0.28, 0.28, cor, op=0.85)
    d.ret_c(cx, cy - 0.4, 0.55, 0.4, '#3a3b3f', op=0.8)
    return cx, cy


def cristal_32k(d, ilhas):
    cx, cy, ang, passo = eixo(ilhas['1'], ilhas['2'])
    d.entrar(mover(cx, cy) * rodar(ang))
    for s in (-1, 1):
        d.ret_c(s * passo / 2, 0, 0.8, 1.2, ESTANHO)
    d.ret_c(0, 0, 3.2, 1.5, 'url(#metal)', rx=0.5, stroke='#7c838b', sw=0.05)
    d.ret_c(0, 0, 2.6, 1.0, None, rx=0.4, stroke='#9aa1a8', sw=0.08)
    d.sair()


def jumper(d, ilhas):
    """O J5 com o jumper de fabrica: a capa preta sobre os dois pinos."""
    cx, cy, ang, passo = eixo(ilhas['1'], ilhas['2'])
    d.entrar(mover(cx, cy) * rodar(ang))
    for s in (-1, 1):
        d.circ(s * passo / 2, 0, 0.82, 'url(#lata)', stroke='#7c838b', sw=0.06)
    d.ret_c(0, 0, passo + 2.4, 2.4, 'url(#plastico)', rx=0.25, stroke='#0a0a0b', sw=0.08)
    for s in (-1, 1):
        d.ret_c(s * passo / 2, 0, 0.9, 0.9, '#0c0c0d', rx=0.1)
    d.sair()


def pino_macho(d, x, y):
    """A barra de pinos soldada, vista de cima: a mesma do Nano (placas-arduino.py, HEAD)."""
    d.circ(x, y, 0.82, 'url(#lata)', stroke='#7c838b', sw=0.06)
    d.ret_c(x, y, 0.64, 0.64, '#9aa1a8', rx=0.1)


def matriz(d, x, y, lado, n, semente):
    """O codigo 2D gravado na blindagem (um padrao, nao um codigo de verdade)."""
    rnd = random.Random(semente)
    c = lado / n
    d.ret(x, y, lado, lado, '#c3c7cc')
    for i in range(n):
        for j in range(n):
            borda = i == 0 or j == n - 1
            if borda or rnd.random() < 0.48:
                d.ret(x + i * c, y + j * c, c * 1.02, c * 1.02, '#3d4148')


def modulo(d, m, ilhas):
    """O modulo: a placa dele (com as ilhas meio-furo nas bordas), a
    blindagem de metal gravada e a antena de trilha na ponta."""
    x0, y0, x1, y1 = m['caixa']
    yb = m['blindagem']                 # o y onde termina a blindagem (comeca a antena)
    # as ilhas da placa que aparecem em volta do modulo (as das bordas)
    if m.get('castelado'):
        for p in ilhas.values():
            dx, dy = min(p[0] - x0, x1 - p[0]), p[1] - y0
            if min(dx, dy) < 1.2:
                if dx < 1.2:
                    xx = x0 if p[0] - x0 < x1 - p[0] else x1
                    d.ret_c(xx, p[1], 1.5, 0.9, ESTANHO, rx=0.1)
                else:
                    d.ret_c(p[0], y0, 0.9, 1.5, ESTANHO, rx=0.1)
    d.ret(x0, y0, x1 - x0, y1 - y0, SUBSTRATO, rx=0.3, stroke='#0b0c0e', sw=0.08)
    if m.get('castelado'):
        for p in ilhas.values():
            dx, dy = min(p[0] - x0, x1 - p[0]), p[1] - y0
            if dx < 1.2:
                xx = x0 if p[0] - x0 < x1 - p[0] else x1
                d.circ(xx, p[1], 0.33, OURO)
            elif dy < 1.2:
                d.circ(p[0], y0, 0.33, OURO)
    # a antena: a trilha em zigue-zague sob a mascara do modulo
    meio = (x0 + x1) / 2
    ya, yt = yb + 0.6, y1 - 0.7
    pts = [(x0 + 1.2, ya), (x0 + 1.2, yt)]
    n = 7
    larg = (x1 - x0 - 2.4)
    for k in range(n):
        xa = x0 + 1.2 + larg * (k + 0.5) / n
        xb = x0 + 1.2 + larg * (k + 1) / n
        pts += [(xa, yt), (xa, ya + 1.4), (xb, ya + 1.4), (xb, yt)] if k % 2 == 0 else [(xa, yt), (xb, yt)]
    d.poli(pts, None, '#22252a', 0.45, fechado=False, cap='round')
    d.poli(pts, None, '#2a2e34', 0.16, fechado=False, cap='round')
    # a blindagem
    d.ret(x0 + 0.25, y0 + 0.25, x1 - x0 - 0.5, yb - y0 - 0.25, 'url(#blindagem)', rx=0.35, stroke='#8c939b', sw=0.08)
    d.ret(x0 + 0.65, y0 + 0.65, x1 - x0 - 1.3, yb - y0 - 1.05, None, rx=0.3, stroke='#b9bfc6', sw=0.08)
    w = x1 - x0
    cor = '#4a4f56'
    topo = yb - 1.5
    d.texto(x0 + 1.0, topo - 0.3, m['nome'], m.get('fonte', 1.0), cor, alinhar='left')
    for k, linha in enumerate(m.get('linhas', [])):
        d.texto(x0 + 1.0, topo - 1.9 - k * 1.0, linha, 0.62, cor, peso='normal', alinhar='left')
    lado = m.get('matriz', 4.0)
    matriz(d, x1 - 1.0 - lado, topo - 0.6 - lado, lado, 14, m['nome'])
    d.circ(x0 + 1.2, y0 + 1.3, 0.3, '#5d636b')                    # a marca do pino 1
    d.texto(x1 - 1.0, y0 + 1.1, m.get('lote', ''), 0.6, cor, peso='normal', alinhar='right')


# =====================================================================
#  a serigrafia em volta de cada peca (o desenho e nosso: um contorno no
#  lugar e no tamanho de cada uma, como as DevKits trazem)
# =====================================================================
def contorno(d, cx, cy, w, h, ang=0.0, chanfro=0.0):
    d.entrar(mover(cx, cy) * rodar(ang))
    if chanfro:
        x, y = w / 2, h / 2
        d.poli([(-x + chanfro, y), (x, y), (x, -y), (-x, -y), (-x, y - chanfro)], None, SERIGRAFIA, 0.12)
    else:
        d.ret_c(0, 0, w, h, None, stroke=SERIGRAFIA, sw=0.12)
    d.sair()


def colchetes(d, il, folga_x, larg):
    """Os dois colchetes em volta de um resistor ou capacitor pequeno: [ ]."""
    cx, cy, ang, passo = eixo(il['1'], il['2'])
    d.entrar(mover(cx, cy) * rodar(ang))
    x, y, a = passo / 2 + folga_x, larg / 2, 0.3
    for s_ in (-1, 1):
        d.poli([(s_ * (x - a), y), (s_ * x, y), (s_ * x, -y), (s_ * (x - a), -y)], None, SERIGRAFIA, 0.12, fechado=False)
    d.sair()


def cantos(d, cx, cy, lado):
    """As quatro quinas em volta de um QFN."""
    m, a = lado / 2, 0.6
    for sx in (-1, 1):
        for sy in (-1, 1):
            d.poli([(cx + sx * (m - a), cy + sy * m), (cx + sx * m, cy + sy * m), (cx + sx * m, cy + sy * (m - a))],
                   None, SERIGRAFIA, 0.12, fechado=False)


def serigrafia_das_pecas(d, ilhas, ref_mod, usb):
    for ref, il in sorted(ilhas.items()):
        tipo = ref.rstrip('0123456789')
        if ref in ('J1', 'J3', ref_mod):
            continue
        n = len(il)
        cx, cy, w, h = caixa(il)
        _, _, ang, passo = eixo(il['1'], il['2']) if n >= 2 else (0, 0, 0, 0)
        if tipo in ('R', 'C') and n == 2:
            colchetes(d, il, 0.55 if passo < 1.2 else 0.75, 0.95 if passo < 1.2 else 1.35)
        elif tipo == 'D' and n == 2:
            if 1.5 < passo < 1.8:
                contorno(d, cx, cy, passo + 1.2, 1.3, ang)
            elif passo > 2.5:
                contorno(d, cx, cy, passo + 1.6, 2.2, ang)
            else:
                contorno(d, cx, cy, passo + 1.0, 1.1, ang)
        elif tipo == 'D' and n == 4:
            contorno(d, cx, cy, w + 1.5 if w > h else 4.1, h + 1.5 if h > w else 4.1, chanfro=0.7)
        elif tipo == 'Q' and n == 3:
            contorno(d, cx, cy, 3.6 if w > h else 3.7, 3.7 if w > h else 3.6)
        elif tipo == 'U' and n == 4:
            contorno(d, cx, cy, w + 2.4, h + 2.6)
        elif tipo == 'U' and n == 6:
            contorno(d, cx, cy, w + 1.0, h + 1.0)
        elif tipo == 'U' and n > 20:
            cantos(d, *caixa({k: v for k, v in il.items() if int(k) <= 28})[:2], 5.9)
        elif tipo == 'X':
            contorno(d, cx, cy, passo + 1.8, 2.1, ang)
        elif tipo == 'J' and n == 2:
            contorno(d, cx, cy, passo + 2.8, 2.8, ang)


# =====================================================================
#  as placas
# =====================================================================
ROTULOS_DE_PINO = {'G', 'GND', '3V3', '5V', 'TX', 'RX', 'RST', 'NC', 'VBAT', '13/N', '14/N'}

PLACAS = {
    'esp32-s3-devkit': dict(
        cad='esp32-s3.json', altura=62.74,
        modulo=dict(caixa=(3.75, 43.64, 21.75, 69.14), blindagem=62.35, nome='ESP32-S3-WROOM-1', fonte=1.05,
                    linhas=['N8R8', 'FCC ID: 2AC7Z-ESPS3WROOM1'], lote='E1N8', castelado=True),
        textos={'RGB@IO38': 'RGB@IO48'},
        usb='micro', marcas={'U2': 'SGM2212', 'U3': ['CP2102N', 'A02', '2130']},
        J1=['3V3', '3V3.2', 'EN', '4', '5', '6', '7', '15', '16', '17', '18', '8', '3', '46', '9', '10', '11',
            '12', '13', '14', '5V', 'GND'],
        J3=['GND.2', 'TX', 'RX', '1', '2', '42', '41', '40', '39', '38', '37', '36', '35', '0', '45', '48', '47',
            '21', '20', '19', 'GND.3', 'GND.4'],
        rgb=48, botoes={'SW1': 'BOOT', 'SW2': 'EN'}),
    'esp32-s2-devkit': dict(
        cad='esp32-s2.json', altura=54.0,
        modulo=dict(caixa=(4.98, 38.61, 20.38, 58.61), blindagem=53.51, nome='ESP32-S2-MINI-1', fonte=0.95,
                    linhas=['FCC ID: 2AC7Z-ESPS2MINI1'], lote='XX0152', matriz=3.6),
        usb='micro', marcas={'U2': 'SGM2212', 'U3': ['CP2102N', 'A02', '2021']},
        J1=['3V3', '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', '10', '11', '12', '13', '14', '15', '16', '17',
            '5V', 'GND'],
        J3=['GND.2', 'EN', '46', '45', 'RX', 'TX', '42', '41', '40', '39', '38', '37', '36', '35', '34', '33', '26',
            '21', '20', '19', '18'],
        rgb=18, botoes={'SW1': 'BOOT', 'SW2': 'EN'}),
    'esp32-c3-devkit': dict(
        cad='esp32-c3.json', altura=38.91,
        modulo=dict(caixa=(6.10, 27.66, 19.30, 44.26), blindagem=38.86, nome='ESP32-C3-MINI-1', fonte=0.9,
                    linhas=['FCC ID: 2AC7Z-ESPC3MINI1'], lote='E1N4', matriz=3.2),
        usb='micro', marcas={'U2': 'SGM2212', 'U3': ['CP2102N', 'A02', '2019']},
        J1=['GND', '3V3', '3V3.2', '2', '3', 'GND.2', 'EN', 'GND.3', '0', '1', '10', 'GND.4', '5V', '5V.2', 'GND.5'],
        J3=['GND.6', 'TX', 'RX', 'GND.7', '9', '8', 'GND.8', '7', '6', '5', '4', 'GND.9', '18', '19', 'GND.10'],
        rgb=8, botoes={'SW1': 'BOOT', 'SW2': 'EN'}),
    'esp32-c6-devkit': dict(
        cad='esp32-c6.json', altura=51.8,
        modulo=dict(caixa=(3.71, 32.65, 21.71, 58.15), blindagem=51.36, nome='ESP32-C6-WROOM-1', fonte=1.05,
                    linhas=['FCC ID: 2AC7Z-ESPC6WROOM1'], lote='XXN4', castelado=True),
        usb='c', marcas={'U2': 'SGM2212', 'U3': ['CH343P', '2312']},
        J1=['3V3', 'EN', '4', '5', '6', '7', '0', '1', '8', '10', '11', '2', '3', '5V', 'GND', 'NC'],
        J3=['GND.2', 'TX', 'RX', '15', '23', '22', '21', '20', '19', '18', '9', 'GND.3', '13', '12', 'GND.4', 'NC.2'],
        rgb=8, botoes={'SW1': 'BOOT', 'SW2': 'EN'}),
    'esp32-h2-devkit': dict(
        cad='esp32-h2.json', altura=48.26,
        modulo=dict(caixa=(6.10, 37.06, 19.30, 53.66), blindagem=48.26, nome='ESP32-H2-MINI-1', fonte=0.9,
                    linhas=['FCC ID: 2AC7Z-ESPH2MINI1'], lote='VBN4', matriz=3.2),
        usb='c', marcas={'U2': 'MN4', 'U3': ['CH343P', '2312']},
        J1=['3V3', 'EN', '0', '1', '2', '3', '13', '14', '4', '5', 'NC', 'VBAT', 'GND', '5V', 'GND.2'],
        J3=['GND.3', 'TX', 'RX', '10', '11', '25', '12', '8', '22', 'GND.4', '9', 'GND.5', '27', '26', 'GND.6'],
        rgb=8, botoes={'SW1': 'BOOT', 'SW2': 'EN'}),
}


def gerar(id_peca, cfg):
    dados = json.load(io.open(os.path.join(CAD, cfg['cad']), encoding='utf-8'))
    ilhas = dados['ilhas']
    H = cfg['altura']
    m = cfg['modulo']
    # a extensao do desenho: a placa, a antena para cima e o USB um pouco para baixo
    topo = max(H, m['caixa'][3])
    fundo = -0.6
    L_px, A_px = round(25.4 * A), round((topo - fundo) * A)
    d = Desenho(T(A, 0, 0, -A, 0, topo * A))

    # ---- a placa ----
    d.ret(0, 0, 25.4, H, PRETA, rx=0.6, stroke=PRETA_BORDA, sw=0.12)
    d.ret(0, 0, 25.4, H, 'url(#luz)', rx=0.6)

    # ---- as barras de pinos ----
    pinos = {}
    for barra in ('J1', 'J3'):
        for n, nome in enumerate(cfg[barra], 1):
            x, y = ilhas[barra][str(n)]
            x = 1.27 if barra == 'J1' else 24.13
            pino_macho(d, x, y)
            pinos[nome] = (round(x * A, 2), round((topo - y) * A, 2))

    # ---- a serigrafia (antes das pecas: o que fica embaixo delas some, como na placa) ----
    trocas = cfg.get('textos', {})
    for t in dados['textos']:
        txt = trocas.get(t['t'], t['t'])
        # fora: a legenda da montagem (letras grandes) e os grupos de designadores
        if t['tam'] > 1.6 or re.fullmatch(r'([A-Z]{1,2}\d+\s*)+', txt):
            continue
        d.texto(t['x'], t['y'], txt, t['tam'] * 0.72, SERIGRAFIA, ang=t['ang'], ancora='middle', alinhar='center')
    for ref, t in dados['designadores'].items():
        tipo = ref.rstrip('0123456789')
        if ref in ('J1', 'J3') or tipo in ('R', 'C') or tipo == 'D' and ref not in ('D5', 'D6') or t['tam'] > 1.2:
            continue
        d.texto(t['x'], t['y'], ref, t['tam'] * 0.72, SERIGRAFIA, ang=t['ang'], ancora='middle', alinhar='center',
                peso='normal')

    # ---- as pecas pequenas ----
    centros = {}
    # o modulo: o U de maior tamanho (o QFN do USB pode ter tantas ilhas quanto ele)
    ref_mod = max((r for r in ilhas if r.startswith('U')), key=lambda r: caixa(ilhas[r])[2] * caixa(ilhas[r])[3])
    serigrafia_das_pecas(d, ilhas, ref_mod, cfg['usb'])
    for ref, il in sorted(ilhas.items()):
        tipo = ref.rstrip('0123456789')
        if ref in ('J1', 'J3', ref_mod):
            continue
        n = len(il)
        _, _, _, passo = eixo(il['1'], il['2']) if n >= 2 else (0, 0, 0, 0)
        if tipo == 'R' and n == 2:
            dois(d, il, *((1.0, 0.5) if passo < 1.2 else (1.6, 0.8)), '#1c1c1e',
                 marca=None)
        elif tipo == 'C' and n == 2:
            dois(d, il, *((1.0, 0.5) if passo < 1.2 else (1.6, 0.8) if passo < 1.9 else (2.0, 1.25)), '#b7956a')
        elif tipo == 'D' and n == 2:
            if 1.5 < passo < 1.8:                     # o LED de energia (0603)
                centros['LED'] = led(d, il, '#ff3b30')
            elif passo > 2.5:                         # o diodo de protecao (SOD-123)
                dois(d, il, 2.7, 1.6, '#202124', frac=0.1, banda=-1)
            else:                                     # os de ESD (SOD-523)
                dois(d, il, 1.2, 0.8, '#202124', frac=0.12, banda=-1)
        elif tipo == 'D' and n == 4:
            centros['RGB'] = led_rgb(d, il)
        elif tipo == 'Q' and n == 3:
            sot23(d, il)
        elif tipo == 'U' and n == 4:
            sot223(d, il, cfg['marcas'].get(ref, ''))
        elif tipo == 'U' and n == 6:
            cx, cy, w, h = caixa(il)
            for p in il.values():
                d.ret_c(p[0], p[1], 0.3, 0.55, ESTANHO)
            pa.ci_em(d, cx, cy, 1.6 if w > h else 1.2, 1.2 if w > h else 1.6, [])
        elif tipo == 'U' and n > 20:
            linhas = cfg['marcas'].get(ref, [])
            qfn(d, il, 5.0, [(t, 0.6 if k == 0 else 0.5) for k, t in enumerate(linhas)])
        elif tipo == 'SW':
            centros[ref] = botao(d, il)
        elif tipo == 'X':
            cristal_32k(d, il)
        elif tipo == 'J' and n == 2:
            jumper(d, il)
        elif tipo == 'J':
            (micro_usb if cfg['usb'] == 'micro' else usb_c)(d, il, 0.0)

    # ---- o modulo ----
    modulo(d, m, ilhas[ref_mod])

    svg = (f'<svg xmlns="http://www.w3.org/2000/svg" version="1.1" width="{L_px}px" height="{A_px}px" '
           f'viewBox="0 0 {L_px} {A_px}">\n<defs>\n{pa.DEFS}{DEFS_EXTRA}</defs>\n' + ''.join(d.partes) + '</svg>\n')
    io.open(BASE + f'app/assets/{id_peca}.svg', 'w', encoding='utf-8', newline='').write(svg)
    print(f'{id_peca}: {L_px}x{A_px} px, {len(svg) // 1024} KB')
    for nome, (x, y) in centros.items():
        print(f'  {nome}: ({x * A:.1f}, {(topo - y) * A:.1f}) px da caixa')
    return pinos, (L_px, A_px), {k: (round(x * A, 1), round((topo - y) * A, 1)) for k, (x, y) in centros.items()}


# =====================================================================
#  a ESP32 DevKit V1 (a DOIT de 30 pinos)
# =====================================================================
# Ela nao tem projeto publicado: as medidas sairam de uma foto de cima,
# retificada pelos pinos (o passo de 2,54 mm e a regua), e conferem com o
# desenho antigo. Em mm, y para cima, a origem no canto de baixo-esquerdo da
# placa (28 x 51,4 mm); os pinos ficam onde sempre estiveram na peca (x 5 e
# 101 px, do EN/D23 em y 24 ate o VIN/3V3), so que no passo exato.
DOIT_ESQ = ['EN', 'VP', 'VN', 'D34', 'D35', 'D32', 'D33', 'D25', 'D26', 'D27', 'D14', 'D12', 'D13', 'GND.2', 'VIN']
DOIT_DIR = ['D23', 'D22', 'TX0', 'RX0', 'D21', 'D19', 'D18', 'D5', 'TX2', 'RX2', 'D4', 'D2', 'D15', 'GND.1', '3V3']
DOIT_ROTULOS = {'GND.1': 'GND', 'GND.2': 'GND'}


def par(x, y, comp, vertical=False):
    """As duas ilhas de uma peca de dois terminais com centro (x, y)."""
    h = comp / 2
    return {'1': (x, y - h), '2': (x, y + h)} if vertical else {'1': (x - h, y), '2': (x + h, y)}


def moldura(d, x, y, w, h):
    """O retangulo de serigrafia em volta de cada peca, como a DOIT faz."""
    d.ret(x - w / 2, y - h / 2, w, h, None, stroke=SERIGRAFIA, sw=0.15)


def gerar_doit(id_peca):
    L, H = 28.0, 51.4
    yen = 45.4                                 # o y do EN (e do D23)
    fundo = -0.8
    tx, ty = 5.0 - 1.3 * A, 24.0 + yen * A
    L_px, A_px = 107, round(ty - fundo * A)
    d = Desenho(T(A, 0, 0, -A, tx, ty))

    # ---- a placa, com os quatro furos de fixacao vazados ----
    furos = [(2.2, 49.15), (25.8, 49.15), (2.2, 2.25), (25.8, 2.25)]
    cmds = [('M', 0.6, 0), ('L', L - 0.6, 0), ('A', 0.6, 0, 0, L, 0.6), ('L', L, H - 0.6), ('A', 0.6, 0, 0, L - 0.6, H),
            ('L', 0.6, H), ('A', 0.6, 0, 0, 0, H - 0.6), ('L', 0, 0.6), ('A', 0.6, 0, 0, 0.6, 0), ('Z',)]
    for x, y in furos:
        cmds += [('M', x - 1.0, y), ('A', 1.0, 1, 0, x + 1.0, y), ('A', 1.0, 1, 0, x - 1.0, y), ('Z',)]
    d.caminho(cmds, PRETA, PRETA_BORDA, 0.12)
    d.partes[-1] = d.partes[-1].replace('<path ', '<path fill-rule="evenodd" ', 1)
    for x, y in furos:
        d.circ(x, y, 1.35, None, ESTANHO, 0.7)

    # ---- os pinos e os nomes (de pe, lendo de baixo para cima, por dentro) ----
    pinos = {}
    for lista, x, xr in ((DOIT_ESQ, 1.3, 3.4), (DOIT_DIR, 26.7, 24.6)):
        for k, nome in enumerate(lista):
            y = yen - 2.54 * k
            pino_macho(d, x, y)
            pinos[nome] = (round(5.0 + (x - 1.3) * A, 2), round(24.0 + 2.54 * k * A, 2))
            d.texto(xr, y, DOIT_ROTULOS.get(nome, nome), 0.85, SERIGRAFIA, ang=90, ancora='middle', alinhar='center')

    # ---- a serigrafia das pecas (antes delas) ----
    for x, y, w, h in [(6.5, 23.0, 3.4, 1.9), (6.5, 20.8, 3.4, 1.9), (14.05, 21.9, 10.9, 3.6), (22.2, 22.9, 3.4, 1.9),
                       (22.2, 20.8, 3.4, 1.9), (7.6, 17.8, 5.5, 2.7), (11.55, 17.6, 1.4, 2.8), (22.8, 17.6, 1.4, 3.3),
                       (6.7, 7.9, 3.2, 1.6), (10.2, 7.9, 3.0, 1.6), (13.7, 7.9, 3.2, 1.6), (18.2, 7.9, 3.2, 1.6),
                       (21.8, 7.9, 3.2, 1.6)]:
        moldura(d, x, y, w, h)
    d.poli([(6.3, 9.1), (6.3, 8.8), (12.0, 8.8), (12.0, 9.1)], None, SERIGRAFIA, 0.15, fechado=False)
    d.texto(8.9, 4.1, 'EN', 0.95, SERIGRAFIA, ang=90, ancora='middle', alinhar='center')
    d.texto(18.75, 4.1, 'BOOT', 0.95, SERIGRAFIA, ang=90, ancora='middle', alinhar='center')
    for x, s in ((7.25, 1), (19.85, -1)):
        d.poli([(x, 6.4), (x + s * 0.45, 6.4), (x + s * 0.45, 1.7), (x, 1.7)], None, SERIGRAFIA, 0.15, fechado=False)

    # ---- as pecas pequenas ----
    for y in (23.0, 20.8):
        dois(d, par(6.5, y, 2.0), 2.0, 1.25, '#b7956a')
    centros = {}
    centros['LED'] = led(d, par(9.4, 22.0, 1.6, True), '#ff3b30')
    centros['LED2'] = led(d, par(18.7, 22.0, 1.6, True), '#3b8cff')
    for x in (10.9, 12.4, 14.2, 15.55, 17.05):
        dois(d, par(x, 22.0, 1.6, True), 1.6, 0.8, '#1c1c1e')
    for y in (22.9, 20.8):
        dois(d, par(22.2, y, 2.0), 2.0, 1.25, '#1c1c1e')
    dois(d, par(7.6, 17.8, 3.8), 4.4, 1.9, '#202124', frac=0.1, banda=-1, marca='J107')
    dois(d, par(11.55, 17.6, 1.6, True), 1.6, 0.8, '#1c1c1e')
    dois(d, par(22.8, 17.6, 1.6, True), 1.6, 0.8, '#1c1c1e')
    sot23(d, {'1': (12.8, 18.75), '2': (12.8, 16.85), '3': (15.1, 17.8)})
    sot23(d, {'1': (20.7, 16.85), '2': (20.7, 18.75), '3': (18.4, 17.8)})
    sot223(d, {'1': (12.3, 14.8), '2': (12.3, 12.5), '3': (12.3, 10.2), '4': (6.0, 12.5)}, 'AMS1117')
    qfn_em(d, 19.65, 12.35, 5.0, [('CP2102', 0.62), ('GMR', 0.5), ('1904', 0.45)])
    for x, cor in ((6.7, '#1c1c1e'), (13.7, '#b7956a'), (18.2, '#1c1c1e'), (21.8, '#1c1c1e')):
        dois(d, par(x, 7.9, 2.0), 2.0, 1.25, cor)
    dois(d, par(10.2, 7.9, 2.0), 1.8, 1.25, '#202124', frac=0.12, banda=1)
    for nome, x in (('EN', 5.5), ('BOOT', 21.6)):
        centros[nome] = botao(d, {'1': (x - 1.07, 2.0), '2': (x + 1.07, 2.0), '3': (x + 1.07, 6.15), '4': (x - 1.07, 6.15)})
    micro_usb_em(d, 14.0, 5.8, -0.2)

    # ---- o modulo ESP-WROOM-32 ----
    x0, y0, x1 = 5.0, 25.9, 23.0
    ilhas_mod = {}
    for k in range(14):
        y = y0 + 1.5 + k * 1.27
        ilhas_mod[f'e{k}'] = (x0 + 0.45, y)
        ilhas_mod[f'd{k}'] = (x1 - 0.45, y)
    for k in range(10):
        ilhas_mod[f'b{k}'] = ((x0 + x1) / 2 + (k - 4.5) * 1.27, y0 + 0.45)
    modulo(d, dict(caixa=(x0, y0, x1, H), blindagem=46.0, nome='ESP-WROOM-32', fonte=1.05, castelado=True,
                   linhas=['FCC ID: 2AC7Z-ESPWROOM32', '211-161007'], lote=''), ilhas_mod)

    svg = (f'<svg xmlns="http://www.w3.org/2000/svg" version="1.1" width="{L_px}px" height="{A_px}px" '
           f'viewBox="0 0 {L_px} {A_px}">\n<defs>\n{pa.DEFS}{DEFS_EXTRA}</defs>\n' + ''.join(d.partes) + '</svg>\n')
    io.open(BASE + f'app/assets/{id_peca}.svg', 'w', encoding='utf-8', newline='').write(svg)
    print(f'{id_peca}: {L_px}x{A_px} px, {len(svg) // 1024} KB')
    centros_px = {k: (round(tx + x * A, 1), round(ty - y * A, 1)) for k, (x, y) in centros.items()}
    for nome, (x, y) in centros_px.items():
        print(f'  {nome}: ({x}, {y}) px da caixa')
    return pinos, (L_px, A_px), centros_px


DESCRICOES = {
    'EN': 'EN (o RST da serigrafia): ligado ao GND, segura a placa em reset; solto, ela roda.',
    'NC': 'Nao conectado: o furo existe na barra, mas nao vai a lugar nenhum.',
    'VBAT': 'VBAT: entrada de 3,3 V (ou de uma bateria) para a placa.',
}


def gravar_pinos(id_peca, pinos, tamanho):
    """Grava no pecas.json a caixa e os pinos (no furo de verdade). O que cada
    pino ja tinha (sinais, descricao) fica; os novos (o segundo 3V3, os GND
    a mais, NC, VBAT) entram; os que a placa de verdade nao tem saem."""
    cam = BASE + 'app/assets/pecas.json'
    lista = json.load(io.open(cam, encoding='utf-8'))
    peca = next(p for p in lista if p['id'] == id_peca)
    antigos = {q['nome']: q for q in peca['pinos']}
    novos = []
    for nome, (x, y) in pinos.items():
        q = dict(antigos.get(nome) or {'nome': nome, 'sinais': [], 'descricao': ''})
        base = nome.split('.')[0]
        if nome not in antigos:
            q['sinais'] = ['power'] if base in ('3V3', '5V', 'GND', 'VBAT') else []
        if base in DESCRICOES and not q.get('descricao'):
            q['descricao'] = DESCRICOES[base]
        q['x'], q['y'] = x, y
        novos.append(q)
    saem = sorted(set(antigos) - set(pinos))
    peca['largura'], peca['altura'], peca['pinos'] = tamanho[0], tamanho[1], novos
    io.open(cam, 'w', encoding='utf-8', newline='').write(json.dumps(lista, ensure_ascii=False, indent=1))
    print(f'  pecas.json: caixa {tamanho[0]}x{tamanho[1]}, {len(novos)} pinos'
          + (f'; sairam {saem}' if saem else '') + f'; novos {sorted(set(pinos) - set(antigos))}')


if __name__ == '__main__':
    for id_peca in (sys.argv[1:] or list(PLACAS) + ['esp32-devkit-v1']):
        if id_peca == 'esp32-devkit-v1':
            pinos, tamanho, _ = gerar_doit(id_peca)
        else:
            pinos, tamanho, _ = gerar(id_peca, PLACAS[id_peca])
        gravar_pinos(id_peca, pinos, tamanho)
