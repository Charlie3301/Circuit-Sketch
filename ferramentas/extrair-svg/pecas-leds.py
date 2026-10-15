"""python ferramentas/extrair-svg/pecas-leds.py

A familia dos LEDs redesenhada (Alpha 25), no estilo das placas
(placas-arduino.py: as mesmas primitivas e a mesma paleta de metal, plastico
e serigrafia), na escala real (96 px por polegada):

  - led-<cor>-off/on.svg: o LED de 5 mm (T-1 3/4) visto de lado, com o epoxi
    difuso da cor, a aba com o chanfro do catodo, a bigorna (catodo) e o
    poste (anodo) aparecendo por dentro, e as pernas ate o furo;
  - rgb-led.svg: o RGB de 5 mm, difuso, quatro pernas (o comum mais comprido);
  - led-bar-graph.svg: a barra de 10 segmentos (DIP-20, 25,4 x 10,1 mm);
  - 7segment.svg: o digito de 0,56" (face preta, segmentos brancos, ponto);
  - neopixel.svg: o WS2812B 5050 visto de cima;
  - led-ring.svg: o anel de 16 WS2812B (44,5 mm por fora, 31,75 por dentro);
  - neopixel-matrix.svg: a matriz 8x8 de WS2812B (65 x 65 mm, passo de 8 mm).

Os pinos de cada peca ficam onde estavam (os projetos nao mudam), salvo no
anel e na matriz, que passaram para o tamanho real (a barra de 4 pinos fica
embaixo). O que acende por cima do desenho (os segmentos, cada pixel, o
brilho do LED) sai daqui tambem: app/DesenhoDosLeds.cs e gerado junto, com a
geometria de cada segmento e o centro de cada pixel.
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

Desenho, T, mover, rodar = pa.Desenho, pa.T, pa.mover, pa.rodar
SERIGRAFIA, ESTANHO, PRETO_CI, MARCA_CI, FURO = pa.SERIGRAFIA, pa.ESTANHO, pa.PRETO_CI, pa.MARCA_CI, pa.FURO
BASE = pa.BASE
A = 96 / 25.4          # px por mm
f = pa.f

# =====================================================================
#  as cores do epoxi (difuso) de cada LED: a apagada e a acesa
# =====================================================================
CORES = {
    #           apagado    sombra     acesa      miolo aceso
    'vermelho': ('#c2342a', '#7e1610', '#ff4b38', '#fff0e4'),
    'verde':    ('#2f9a45', '#145a24', '#4ff06a', '#f2fff2'),
    'amarelo':  ('#e0bb24', '#8f6f08', '#ffe03c', '#fffbe8'),
    'azul':     ('#2a4fc4', '#11236e', '#4c8cff', '#eef5ff'),
    'branco':   ('#e9e8e2', '#a9a8a2', '#ffffff', '#ffffff'),
    'laranja':  ('#e07a22', '#8c420a', '#ff9a36', '#fff4e6'),
    'roxo':     ('#8a3dbd', '#4a1670', '#c876ff', '#fbf0ff'),
}


def svg(largura, altura, partes, defs=''):
    return (f'<svg xmlns="http://www.w3.org/2000/svg" version="1.1" width="{f(largura)}px" height="{f(altura)}px" '
            f'viewBox="0 0 {f(largura)} {f(altura)}">\n<defs>\n{pa.DEFS}{defs}</defs>\n' + ''.join(partes) + '</svg>\n')


def gravar(nome, conteudo):
    io.open(BASE + f'app/assets/{nome}.svg', 'w', encoding='utf-8', newline='').write(conteudo)
    print(f'  {nome}.svg: {len(conteudo) // 1024} KB')


def grad_h(id_, paradas):
    s = ''.join(f'<stop offset="{o}" stop-color="{c}"' + (f' stop-opacity="{a}"' if a is not None else '') + '/>'
                for o, c, a in paradas)
    return f'  <linearGradient id="{id_}" x1="0" y1="0" x2="1" y2="0">{s}</linearGradient>\n'


def grad_r(id_, paradas, cx=0.5, cy=0.5, r=0.5):
    s = ''.join(f'<stop offset="{o}" stop-color="{c}"' + (f' stop-opacity="{a}"' if a is not None else '') + '/>'
                for o, c, a in paradas)
    return f'  <radialGradient id="{id_}" cx="{cx}" cy="{cy}" r="{r}">{s}</radialGradient>\n'


# =====================================================================
#  o LED de 5 mm, de lado
# =====================================================================
# Em mm, x a partir do meio das pernas, y para baixo a partir do topo da
# caixa (40 x 50 px). Os pinos do catalogo: C em (15, 42) px, A em (25, 42).
LED_X0 = 20 / A                  # o meio, em mm
LED_PINO_Y = 42 / A              # 11,11 mm
LED_ABA = (9.05, 10.05)          # a aba (flange) de 5,8 mm
LED_TOPO = 1.25                  # o alto da cupula
LED_R = 2.5                      # o raio do corpo (5 mm)
LED_DIE = (0.18, 5.15)           # onde fica o chip (no copo da bigorna), relativo ao meio


def caminho_corpo(d, x0, topo, base, r, fill, op=None, stroke=None, sw=0.0):
    """A cupula e o cilindro do LED: o arco de cima e as duas paredes."""
    yb = topo + r
    d.caminho([('M', x0 - r, base), ('L', x0 - r, yb), ('A', r, 0, 1, x0 + r, yb), ('L', x0 + r, base), ('Z',)],
              fill, stroke, sw, op)


def armacao(d, x0, aceso):
    """A bigorna (catodo, a peca larga com o copo) e o poste (anodo), vistos pelo epoxi."""
    cor = '#9da3aa'
    # a bigorna: sobe da perna do catodo, alarga e faz o copo
    d.poli([(x0 - 1.52, LED_ABA[0] + 0.3), (x0 - 1.52, 7.2), (x0 - 1.9, 6.1), (x0 - 1.7, 5.55), (x0 + 0.65, 5.55),
            (x0 + 0.65, 5.95), (x0 - 0.65, 6.25), (x0 - 1.02, 7.4), (x0 - 1.02, LED_ABA[0] + 0.3)], cor, op=0.75)
    d.poli([(x0 - 0.42, 5.55), (x0 + 0.62, 5.55), (x0 + 0.44, 5.12), (x0 - 0.24, 5.12)], '#c7ccd2', op=0.8)
    # o poste: fino, com a bandeirinha em cima
    d.poli([(x0 + 1.02, LED_ABA[0] + 0.3), (x0 + 1.02, 6.6), (x0 + 1.55, 5.7), (x0 + 1.55, 5.25), (x0 + 1.1, 5.25),
            (x0 + 1.1, 5.6), (x0 + 1.52, 5.6), (x0 + 1.52, LED_ABA[0] + 0.3)], cor, op=0.7)
    # o fio de ouro, do chip ao poste
    dx, dy = LED_DIE
    d.caminho([('M', x0 + dx, dy - 0.1), ('A', 1.1, 0, 1, x0 + 1.3, 5.3)], None, '#e3c46a', 0.06, so=0.9)
    d.ret_c(x0 + dx, dy, 0.32, 0.22, '#fff6dc' if aceso else '#3a3c40')


def gerar_led(cor, aceso):
    claro, escuro, vivo, miolo = CORES[cor]
    d = Desenho(T(A, 0, 0, A, 0, 0))
    x0 = LED_X0
    defs = ''
    base = claro if not aceso else vivo
    defs += grad_h('epoxi', [(0, escuro, None), (0.22, base, None), (0.5, base, None), (0.78, base, None), (1, escuro, None)])
    defs += grad_h('reflexo', [(0, '#ffffff', 0), (0.18, '#ffffff', 0.55), (0.32, '#ffffff', 0), (1, '#ffffff', 0)])
    if aceso:
        defs += grad_r('luzdentro', [(0, miolo, 1), (0.25, miolo, 0.85), (0.6, vivo, 0.45), (1, vivo, 0)],
                       cx=0.5, cy=0.55, r=0.55)

    # as pernas, do furo ate dentro do corpo
    for x in (15 / A, 25 / A):                  # o catodo (C) e o anodo (A), nos pinos
        d.ret(x - 0.25, LED_ABA[0] + 0.3, 0.5, LED_PINO_Y - LED_ABA[0] - 0.3 + 0.35, 'url(#metal-h)')
    armacao(d, x0, aceso)
    # o corpo: o epoxi tingido por cima da armacao (difuso: ela aparece de leve)
    caminho_corpo(d, x0, LED_TOPO, LED_ABA[0], LED_R, 'url(#epoxi)', op=0.86 if not aceso else 0.8)
    if aceso:
        caminho_corpo(d, x0, LED_TOPO, LED_ABA[0], LED_R, 'url(#luzdentro)')
    # a aba, com o chanfro do lado do catodo (a esquerda)
    d.poli([(x0 - 2.55, LED_ABA[0]), (x0 + 2.9, LED_ABA[0]), (x0 + 2.9, LED_ABA[1]), (x0 - 2.55, LED_ABA[1])],
           'url(#epoxi)', op=0.95)
    d.linha(x0 - 2.55, LED_ABA[0] + 0.02, x0 + 2.9, LED_ABA[0] + 0.02, escuro, 0.1, so=0.6)
    # o brilho do plastico
    caminho_corpo(d, x0, LED_TOPO, LED_ABA[0] - 0.15, LED_R, 'url(#reflexo)')
    d.caminho([('M', x0 - 1.6, LED_TOPO + 1.0), ('A', 2.0, 0, 1, x0 - 0.2, LED_TOPO + 0.25)], None, '#ffffff', 0.32,
              so=0.55 if not aceso else 0.8, cap='round')
    caminho_corpo(d, x0, LED_TOPO, LED_ABA[0], LED_R, None, stroke=escuro, sw=0.08)
    return svg(40, 50, d.partes, defs)


# =====================================================================
#  o LED RGB de 5 mm (quatro pernas, difuso)
# =====================================================================
# Caixa 42 x 73 px. Pinos: R (11,9 50), COM (19 54), G (25,4 50), B (32,4 50).
RGB_PINOS = {'R': (11.9, 50.0), 'COM': (19.0, 54.0), 'G': (25.4, 50.0), 'B': (32.4, 50.0)}
RGB_X0 = (11.9 + 32.4) / 2 / A
RGB_TOPO, RGB_ABA = 1.0, (8.8, 9.8)
RGB_SAIDAS = {'R': -1.9, 'COM': -0.64, 'G': 0.64, 'B': 1.9}   # onde cada perna sai da aba (mm, do meio)


def gerar_rgb():
    d = Desenho(T(A, 0, 0, A, 0, 0))
    x0 = RGB_X0
    defs = grad_h('epoxi', [(0, '#b9b8b3', None), (0.25, '#efeee9', None), (0.55, '#f6f5f1', None), (0.8, '#ebeae5', None),
                            (1, '#b4b3ae', None)])
    defs += grad_h('reflexo', [(0, '#ffffff', 0), (0.18, '#ffffff', 0.6), (0.32, '#ffffff', 0), (1, '#ffffff', 0)])
    # as pernas: saem juntas da aba e abrem ate o furo de cada uma
    for nome, dx in RGB_SAIDAS.items():
        px, py = RGB_PINOS[nome]
        xa, ya = x0 + dx, RGB_ABA[1]
        xb, yb = px / A, py / A
        meio = ya + 1.2
        d.caminho([('M', xa, RGB_ABA[0]), ('L', xa, meio), ('L', xb, meio + 1.6), ('L', xb, yb + 0.35)], None,
                  '#9ea4ab', 0.45, cap='butt')
        d.caminho([('M', xa - 0.08, RGB_ABA[0]), ('L', xa - 0.08, meio), ('L', xb - 0.08, meio + 1.6),
                   ('L', xb - 0.08, yb + 0.35)], None, '#e3e6e9', 0.12, cap='butt', so=0.8)
    # por dentro: o copo com os tres chips, ligado as pernas
    for nome, dx in RGB_SAIDAS.items():
        d.ret(x0 + dx - 0.2, 5.6, 0.4, RGB_ABA[0] - 5.4, '#9da3aa', op=0.55)
    d.poli([(x0 - 1.3, 5.6), (x0 + 1.3, 5.6), (x0 + 0.95, 4.85), (x0 - 0.95, 4.85)], '#c7ccd2', op=0.65)
    for k, cor in enumerate(('#d23b2e', '#3ca64f', '#3a62d1')):
        d.ret_c(x0 - 0.55 + k * 0.55, 5.05, 0.3, 0.22, cor, op=0.8)
    caminho_corpo(d, x0, RGB_TOPO, RGB_ABA[0], LED_R, 'url(#epoxi)', op=0.88)
    d.poli([(x0 - 2.55, RGB_ABA[0]), (x0 + 2.9, RGB_ABA[0]), (x0 + 2.9, RGB_ABA[1]), (x0 - 2.55, RGB_ABA[1])],
           'url(#epoxi)', op=0.97)
    d.linha(x0 - 2.55, RGB_ABA[0] + 0.02, x0 + 2.9, RGB_ABA[0] + 0.02, '#a3a29d', 0.1)
    caminho_corpo(d, x0, RGB_TOPO, RGB_ABA[0] - 0.15, LED_R, 'url(#reflexo)')
    d.caminho([('M', x0 - 1.6, RGB_TOPO + 1.0), ('A', 2.0, 0, 1, x0 - 0.2, RGB_TOPO + 0.25)], None, '#ffffff', 0.32,
              so=0.6, cap='round')
    caminho_corpo(d, x0, RGB_TOPO, RGB_ABA[0], LED_R, None, stroke='#9c9b96', sw=0.08)
    return svg(42, 73, d.partes, defs)


# =====================================================================
#  a barra de 10 LEDs (DIP-20)
# =====================================================================
# Caixa 38 x 96 px; os pinos A1..A10 em x 4,8 e C1..C10 em x 33,38, de
# 9,6 em 9,6 px a partir de y 4,8. O corpo de 10,1 x 25,4 mm cobre tudo.
def segmentos_da_barra():
    cx = 19.09 / A                      # o meio entre as duas fileiras
    w, h = 5.1, 1.6
    return [[(cx - w / 2, (4.8 + 9.6 * k) / A - h / 2), (cx + w / 2, (4.8 + 9.6 * k) / A + h / 2)] for k in range(10)]


def gerar_barra():
    d = Desenho(T(A, 0, 0, A, 0, 0))
    cx = 19.09 / A
    L, H = 10.1, 25.4
    x0, y0 = cx - L / 2, 0.0
    defs = grad_h('lado', [(0, '#151517', None), (0.06, '#2c2d31', None), (0.94, '#2c2d31', None), (1, '#151517', None)])
    # o corpo, com o chanfro do pino 1 (em cima, a esquerda)
    c = 0.8
    d.poli([(x0 + c, y0), (x0 + L, y0), (x0 + L, y0 + H), (x0, y0 + H), (x0, y0 + c)], 'url(#lado)', '#0d0d0f', 0.1)
    d.ret(x0 + 0.5, y0 + 0.4, L - 1.0, H - 0.8, None, rx=0.2, stroke='#3a3b40', sw=0.08)
    # os segmentos: a janela difusa de cada LED
    for (xa, ya), (xb, yb) in segmentos_da_barra():
        d.ret(xa - 0.12, ya - 0.12, xb - xa + 0.24, yb - ya + 0.24, '#0e0e10', rx=0.1)
        d.ret(xa, ya, xb - xa, yb - ya, 'url(#difusor)', rx=0.08)
    # a marca do pino 1 e o codigo, de leve
    d.circ(x0 + 1.1, y0 + 1.6, 0.28, '#4a4b50')
    defs += '  <linearGradient id="difusor" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#e2dcd6"/><stop offset="1" stop-color="#bdb6af"/></linearGradient>\n'
    return svg(38, 96, d.partes, defs)


# =====================================================================
#  o display de 7 segmentos (0,56")
# =====================================================================
SEG_CX, SEG_CY = 47 / 2 / A, 10.0       # o meio do digito (mm)
SEG_ALT, SEG_LARG, SEG_GROSSO, SEG_INCL = 14.2, 7.6, 1.45, math.tan(math.radians(9))


def poligonos_7seg():
    """Os segmentos A..G e o ponto, em mm (inclinados como os de verdade)."""
    h, w, t = SEG_ALT, SEG_LARG, SEG_GROSSO
    g = 0.22                  # a folga entre segmentos
    xe, xd = -w / 2, w / 2
    ys, ym, yi = -h / 2, 0.0, h / 2

    def horiz(y):
        a, b = xe + t / 2 + g, xd - t / 2 - g
        return [(a, y), (a + t / 2, y - t / 2), (b - t / 2, y - t / 2), (b, y), (b - t / 2, y + t / 2), (a + t / 2, y + t / 2)]

    def vert(x, y1, y2):
        a, b = y1 + t / 2 + g, y2 - t / 2 - g
        return [(x, a), (x + t / 2, a + t / 2), (x + t / 2, b - t / 2), (x, b), (x - t / 2, b - t / 2), (x - t / 2, a + t / 2)]

    segs = {'A': horiz(ys + t / 2), 'G': horiz(ym), 'D': horiz(yi - t / 2),
            'F': vert(xe + t / 2, ys, ym), 'B': vert(xd - t / 2, ys, ym),
            'E': vert(xe + t / 2, ym, yi), 'C': vert(xd - t / 2, ym, yi)}
    # a inclinacao: quanto mais alto, mais para a direita
    out = {}
    for nome, pts in segs.items():
        out[nome] = [(SEG_CX + x - y * SEG_INCL - 0.5, SEG_CY + y) for x, y in pts]
    return out


def ponto_7seg():
    return (SEG_CX + SEG_LARG / 2 + 0.9 - (SEG_ALT / 2) * SEG_INCL - 0.5 + 0.35, SEG_CY + SEG_ALT / 2 - 0.65, 0.62)


def gerar_7seg():
    d = Desenho(T(A, 0, 0, A, 0, 0))
    L, H = 47 / A, 20.0
    defs = '  <linearGradient id="face" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#2a2b2f"/><stop offset="1" stop-color="#18191b"/></linearGradient>\n'
    defs += '  <linearGradient id="segmento" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#eeece7"/><stop offset="1" stop-color="#c9c5bf"/></linearGradient>\n'
    # as pernas aparecendo na borda de cima e de baixo
    for x in (4.52, 14.12, 23.72, 33.32, 42.92):
        d.ret(x / A - 0.25, -0.0, 0.5, 0.5, 'url(#metal-h)')
        d.ret(x / A - 0.25, H - 0.5, 0.5, 0.5, 'url(#metal-h)')
    d.ret(0.05, 0.3, L - 0.1, H - 0.6, '#101113', rx=0.2)                  # o corpo, visto de cima
    d.ret(0.3, 0.55, L - 0.6, H - 1.1, 'url(#face)', rx=0.15, stroke='#34353a', sw=0.08)
    for nome, pts in poligonos_7seg().items():
        d.poli(pts, 'url(#segmento)', '#9e9a94', 0.05)
    x, y, r = ponto_7seg()
    d.circ(x, y, r, 'url(#segmento)', '#9e9a94', 0.05)
    return svg(47, round(H * A), d.partes, defs)


# =====================================================================
#  o WS2812B (5050), de cima
# =====================================================================
def ws2812(d, cx, cy, ang=0.0, lado=5.0, pads=True):
    """O 5050 de cima: o corpo branco, o copo refletor, os tres chips e o CI."""
    d.entrar(mover(cx, cy) * rodar(ang))
    m = lado / 2
    if pads:
        for sx in (-1, 1):
            for sy in (-1, 1):
                d.ret_c(sx * (m + 0.05), sy * 1.6, 0.7, 1.0, ESTANHO, rx=0.08)
    d.ret_c(0, 0, lado, lado, '#f2f1ec', rx=0.25, stroke='#c8c5bc', sw=0.06)
    d.poli([(-m, -m + 0.7), (-m + 0.7, -m), (-m, -m)], '#c8c5bc')       # o chanfro do pino 1
    d.circ(0, 0, lado * 0.36, 'url(#copo)', stroke='#d8d2c2', sw=0.05)
    for k, cor in enumerate(('#c9483c', '#3f9a50', '#3d66c9')):
        d.ret_c(-0.55 + k * 0.55, -0.45, 0.32, 0.32, cor, op=0.8)
    d.ret_c(0, 0.45, 0.85, 0.6, '#3a3b3f', op=0.85)
    d.sair()


DEFS_COPO = '  <radialGradient id="copo" cx="0.5" cy="0.45" r="0.55"><stop offset="0" stop-color="#fbfaf6"/><stop offset="0.75" stop-color="#ece8dc"/><stop offset="1" stop-color="#d6d0c0"/></radialGradient>\n'


def gerar_neopixel():
    # caixa 21 x 19 px; o corpo de 5 x 5 mm no meio dos quatro pinos
    d = Desenho(T(A, 0, 0, A, 0, 0))
    for x, y in ((1, 3.5), (1, 14), (21, 14), (21, 3.5)):        # as ilhas, nos quatro pinos
        d.ret_c(x / A + (0.25 if x < 10 else -0.25), y / A, 0.75, 1.0, ESTANHO, rx=0.08)
    ws2812(d, 11 / A, 9.5 / A, lado=5.0, pads=False)
    return svg(21, 19, d.partes, DEFS_COPO)


# =====================================================================
#  o anel de 16 e a matriz 8x8
# =====================================================================
MASCARA_PRETA = '#1c1d20'


def barra_de_4(d, x0, y, nomes):
    """A barrinha de 4 pinos (de 2,54 em 2,54 mm), com o nome de cada um."""
    for k, nome in enumerate(nomes):
        x = x0 + k * 2.54
        d.circ(x, y, 0.82, 'url(#lata)', stroke='#7c838b', sw=0.06)
        d.ret_c(x, y, 0.64, 0.64, '#9aa1a8', rx=0.1)
        d.texto(x, y - 1.55, nome, 0.62, SERIGRAFIA, ancora='middle', alinhar='center')


def gerar_anel():
    R_EXT, R_INT = 44.5 / 2, 31.75 / 2
    aba = 5.2                                  # a lingueta da barra, embaixo
    L, H = 2 * R_EXT, 2 * R_EXT + aba - 1.0
    cx, cy = R_EXT, R_EXT
    d = Desenho(T(A, 0, 0, A, 0, 0))
    # a placa: o anel e a lingueta com os quatro furos
    ly0, ly1 = cy + R_EXT - 2.2, H
    lx0, lx1 = cx - 6.4, cx + 6.4
    d.caminho([('M', cx + R_EXT, cy), ('A', R_EXT, 0, 1, cx - R_EXT, cy), ('A', R_EXT, 0, 1, cx + R_EXT, cy), ('Z',),
               ('M', cx + R_INT, cy), ('A', R_INT, 0, 0, cx - R_INT, cy), ('A', R_INT, 0, 0, cx + R_INT, cy), ('Z',)],
              MASCARA_PRETA, '#0e0f11', 0.15)
    d.partes[-1] = d.partes[-1].replace('<path ', '<path fill-rule="evenodd" ', 1)
    d.ret(lx0, ly0, lx1 - lx0, ly1 - ly0, MASCARA_PRETA, rx=0.8, stroke='#0e0f11', sw=0.15)
    d.ret(lx0 + 0.15, ly0, lx1 - lx0 - 0.3, 2.4, MASCARA_PRETA)
    centros = []
    for k in range(16):
        ang = -90 + k * 360 / 16                 # o pixel 0 em cima, no sentido do relogio
        rr = (R_EXT + R_INT) / 2
        x, y = cx + rr * math.cos(math.radians(ang)), cy + rr * math.sin(math.radians(ang))
        ws2812(d, x, y, ang + 90, lado=5.0)
        # o capacitor de 100 nF ao lado de cada LED
        xc = cx + rr * math.cos(math.radians(ang + 11.25))
        yc = cy + rr * math.sin(math.radians(ang + 11.25))
        d.entrar(mover(xc, yc) * rodar(ang + 90 + 90))
        d.ret_c(0, 0, 1.0, 0.5, '#b7956a')
        d.ret(-0.5, -0.25, 0.22, 0.5, 'url(#metal)')
        d.ret(0.28, -0.25, 0.22, 0.5, 'url(#metal)')
        d.sair()
        centros.append((x, y))
    nomes = ['GND', '5V', 'DIN', 'DOUT']
    x0 = cx - 1.5 * 2.54
    yp = H - 1.6
    barra_de_4(d, x0, yp, nomes)
    pinos = {['GND', 'VCC', 'DIN', 'DOUT'][k]: (round((x0 + k * 2.54) * A, 2), round(yp * A, 2)) for k in range(4)}
    return svg(L * A, H * A, d.partes, DEFS_COPO), centros, pinos, (L * A, H * A)


def gerar_matriz():
    L = 65.0
    aba = 4.6
    H = L + aba
    d = Desenho(T(A, 0, 0, A, 0, 0))
    d.ret(0, 0, L, L, MASCARA_PRETA, rx=0.6, stroke='#0e0f11', sw=0.15)
    d.ret(L / 2 - 6.4, L - 2.0, 12.8, aba + 2.0, MASCARA_PRETA, rx=0.8, stroke='#0e0f11', sw=0.15)
    d.ret(L / 2 - 6.25, L - 2.2, 12.5, 2.4, MASCARA_PRETA)
    passo = 8.0
    m0 = (L - 7 * passo) / 2
    centros = []
    for lin in range(8):
        for col in range(8):
            x, y = m0 + col * passo, m0 + lin * passo
            ws2812(d, x, y, 0, lado=5.0)
            d.entrar(mover(x + 3.4, y - 1.6) * rodar(90))
            d.ret_c(0, 0, 1.0, 0.5, '#b7956a')
            d.ret(-0.5, -0.25, 0.22, 0.5, 'url(#metal)')
            d.ret(0.28, -0.25, 0.22, 0.5, 'url(#metal)')
            d.sair()
            centros.append((x, y))
    nomes = ['GND', '5V', 'DIN', 'DOUT']
    x0 = L / 2 - 1.5 * 2.54
    yp = H - 1.4
    barra_de_4(d, x0, yp, nomes)
    pinos = {['GND', 'VCC', 'DIN', 'DOUT'][k]: (round((x0 + k * 2.54) * A, 2), round(yp * A, 2)) for k in range(4)}
    return svg(L * A, H * A, d.partes, DEFS_COPO), centros, pinos, (L * A, H * A)


# =====================================================================
#  o pecas.json e a geometria para o C#
# =====================================================================
def gravar_pecas(mudancas):
    cam = BASE + 'app/assets/pecas.json'
    lista = json.load(io.open(cam, encoding='utf-8'))
    for p in lista:
        if p['id'] in mudancas:
            tam, pinos = mudancas[p['id']]
            p['largura'], p['altura'] = round(tam[0]), round(tam[1])
            for q in p['pinos']:
                if pinos and q['nome'] in pinos:
                    q['x'], q['y'] = pinos[q['nome']]
            print(f"  pecas.json: {p['id']} {p['largura']}x{p['altura']}")
    io.open(cam, 'w', encoding='utf-8', newline='').write(json.dumps(lista, ensure_ascii=False, indent=1))


def cs_pontos(pts):
    return ', '.join(f'({x * A:.2f}, {y * A:.2f})' for x, y in pts)


def gerar_cs(anel, matriz):
    linhas = []
    w = linhas.append
    w('namespace Simulador.App;')
    w('')
    w('// GERADO por ferramentas/extrair-svg/pecas-leds.py -- nao edite a mao.')
    w('//')
    w('// Onde fica o que acende em cada peca de LED (px da caixa da peca): o')
    w('// contorno de cada segmento do display e da barra, o centro e o tamanho')
    w('// de cada pixel do NeoPixel, do anel e da matriz, e o centro do brilho')
    w('// dos LEDs de 5 mm. O desenho de cada um sai do mesmo script.')
    w('static class DesenhoDosLeds')
    w('{')
    w('    /// <summary>O centro do chip (de onde sai o brilho) do LED e do RGB.</summary>')
    w('    public static readonly Dictionary<string, (double X, double Y)> CentroDoBrilho = new()')
    w('    {')
    w(f'        ["led"] = ({(LED_X0 + LED_DIE[0]) * A:.2f}, {LED_DIE[1] * A:.2f}),')
    w(f'        ["rgb-led"] = ({RGB_X0 * A:.2f}, {5.05 * A:.2f}),')
    w('    };')
    w('')
    w('    /// <summary>O contorno de cada segmento que acende (o nome e o do pino que o comanda).</summary>')
    w('    public static readonly Dictionary<string, Dictionary<string, (double X, double Y)[]>> Segmentos = new()')
    w('    {')
    w('        ["7segment"] = new()')
    w('        {')
    for nome, pts in poligonos_7seg().items():
        w(f'            ["{nome}"] = [{cs_pontos(pts)}],')
    x, y, r = ponto_7seg()
    circ = [(x + r * math.cos(t * math.pi / 8), y + r * math.sin(t * math.pi / 8)) for t in range(16)]
    w(f'            ["DP"] = [{cs_pontos(circ)}],')
    w('        },')
    w('        ["led-bar-graph"] = new()')
    w('        {')
    for k, ((xa, ya), (xb, yb)) in enumerate(segmentos_da_barra(), 1):
        w(f'            ["A{k}"] = [{cs_pontos([(xa, ya), (xb, ya), (xb, yb), (xa, yb)])}],')
    w('        },')
    w('    };')
    w('')
    w('    /// <summary>O centro de cada pixel enderecavel, na ordem da fita, e o raio da luz.</summary>')
    w('    public static readonly Dictionary<string, ((double X, double Y)[] Centros, double Raio)> Pixels = new()')
    w('    {')
    w(f'        ["neopixel"] = ([({11:.2f}, {9.5:.2f})], {2.5 * A:.2f}),')
    w(f'        ["led-ring"] = ([{cs_pontos(anel)}], {2.5 * A:.2f}),')
    w(f'        ["neopixel-matrix"] = ([{cs_pontos(matriz)}], {2.5 * A:.2f}),')
    w('    };')
    w('}')
    io.open(BASE + 'app/DesenhoDosLeds.cs', 'w', encoding='utf-8', newline='').write('\n'.join(linhas) + '\n')
    print('  app/DesenhoDosLeds.cs')


if __name__ == '__main__':
    for cor in CORES:
        for aceso in (False, True):
            gravar(f'led-{cor}-{"on" if aceso else "off"}', gerar_led(cor, aceso))
    gravar('led', gerar_led('vermelho', False))
    gravar('rgb-led', gerar_rgb())
    gravar('led-bar-graph', gerar_barra())
    gravar('7segment', gerar_7seg())
    gravar('neopixel', gerar_neopixel())
    s_anel, c_anel, p_anel, t_anel = gerar_anel()
    gravar('led-ring', s_anel)
    s_mat, c_mat, p_mat, t_mat = gerar_matriz()
    gravar('neopixel-matrix', s_mat)
    gravar_pecas({'7segment': ((47, 20.0 * A), None), 'led-ring': (t_anel, p_anel), 'neopixel-matrix': (t_mat, p_mat)})
    gerar_cs(c_anel, c_mat)
