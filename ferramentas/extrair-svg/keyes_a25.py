"""A placa preta de 18,5 x 15 mm dos modulos KY de tres pinos (KY-013, KY-020 e
os parecidos), medida nas fotos de cima do KY-013 e do KY-020: a mesma furacao
"universal" nos dois (as ilhas redondas, as ovais, as grandes e as de
fixacao), o R1 (0805, 103) ao lado da barra, os "-" e "S" da serigrafia e a
barra de tres pinos a 90 graus na borda direita. O componente de cada modulo
(o NTC, a chave de bolinha) e desenhado por quem chama.

Tudo em mm, a partir do canto de cima da placa (x0, y0), com a barra a
direita (como na peca).
"""
import importlib.util
import math
import os

AQUI = os.path.dirname(os.path.abspath(__file__))
_spec = importlib.util.spec_from_file_location('estilo_a25', os.path.join(AQUI, 'estilo_a25.py'))
E = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(E)
pa = E.pa
ESTANHO, SERIGRAFIA, FURO = pa.ESTANHO, pa.SERIGRAFIA, pa.FURO
PRETA, PRETA_BORDA = '#18191c', '#08090a'

# a furacao, no quadro da placa (x para a direita, y para baixo; a barra a direita)
ILHAS_PEQUENAS = [(1.5, 3.8), (1.5, 11.4), (14.8, 4.0), (14.8, 11.4)]
OVAIS = [(5.4, 5.1), (5.4, 10.0), (11.9, 5.1), (11.9, 10.0)]
GRANDES = [(8.6, 3.8), (8.6, 11.4)]
FIXACAO = [(5.3, 2.0), (5.3, 13.4)]
R1 = (13.7, 8.1)


def textura(d, x0, y0, w, h, passo=0.42):
    """O fosco pontilhado da mascara preta (o que se ve de perto nas fotos)."""
    pts = []
    y = y0 + passo / 2
    k = 0
    while y < y0 + h:
        x = x0 + passo / 2 + (passo / 2 if k % 2 else 0)
        while x < x0 + w:
            pts.append((x, y))
            x += passo
        y += passo * 0.866
        k += 1
    cmds = []
    r = 0.07
    for x, y in pts:
        cmds += [('M', x - r, y), ('A', r, 1, 0, x + r, y), ('A', r, 1, 0, x - r, y), ('Z',)]
    d.caminho(cmds, '#2a2c31', op=0.55)


def ilha(d, x, y, r_anel, r_furo):
    d.circ(x, y, r_anel, ESTANHO)
    d.circ(x, y, r_anel, 'url(#luz)')
    d.circ(x, y, r_anel * 0.94, None, '#ffffff', 0.08, op=0.4)
    d.circ(x, y, r_furo, FURO)


def oval(d, x, y, w=2.8, h=1.9, r_furo=0.5):
    d.ret_c(x, y, w, h, ESTANHO, rx=h / 2)
    d.ret_c(x, y, w, h, 'url(#luz)', rx=h / 2)
    d.ret_c(x, y, w * 0.95, h * 0.92, None, rx=h / 2, stroke='#ffffff', sw=0.08)
    d.partes[-1] = d.partes[-1].replace('stroke="#ffffff"', 'stroke="#ffffff" stroke-opacity="0.4"', 1)
    d.circ(x, y, r_furo, FURO)


def placa(d, x0, y0, ys_barra, comp_pinos, simbolo=True, rotulo_r1=True):
    """A placa com a furacao, o R1, a serigrafia e a barra (os pinos saem para a direita)."""
    E.sombra(d, x0, y0, 18.5, 15.0, rx=0.4, op=0.35)
    d.ret(x0, y0, 18.5, 15.0, PRETA, rx=0.4, stroke=PRETA_BORDA, sw=0.15)
    textura(d, x0 + 0.2, y0 + 0.2, 18.1, 14.6)
    d.ret(x0, y0, 18.5, 15.0, 'url(#luz)', rx=0.4)
    S = SERIGRAFIA
    # a serigrafia: o simbolo do sensor no meio, o contorno do R1, o "-" e o "S"
    if simbolo:
        cx, cy = x0 + 8.6, y0 + 7.7
        d.circ(cx, cy, 0.55, None, S, 0.16)
        d.circ(cx, cy, 0.2, S)
        for r in (1.3, 2.0):
            for a0 in (150, -30):
                a, b = math.radians(a0), math.radians(a0 + 60)
                d.caminho([('M', cx + r * math.cos(a), cy + r * math.sin(a)),
                           ('A', r, 0, 1, cx + r * math.cos(b), cy + r * math.sin(b))], None, S, 0.16, cap='round')
    rx, ry = x0 + R1[0], y0 + R1[1]
    d.ret_c(rx, ry, 2.0, 3.9, None, rx=0.15, stroke=S, sw=0.15)
    if rotulo_r1:
        d.texto(rx - 1.7, ry - 2.3, 'R1', 0.85, S, ang=90, alinhar='center', peso='bold')
    d.texto(x0 + 17.1, y0 + 1.6, '-', 1.4, S, alinhar='center', peso='bold')
    d.texto(x0 + 17.0, y0 + 13.6, 'S', 1.2, S, alinhar='center', peso='bold')
    # a furacao
    for x, y in ILHAS_PEQUENAS:
        ilha(d, x0 + x, y0 + y, 0.95, 0.45)
    for x, y in OVAIS:
        oval(d, x0 + x, y0 + y)
    for x, y in GRANDES:
        ilha(d, x0 + x, y0 + y, 1.15, 0.75)
    for x, y in FIXACAO:
        ilha(d, x0 + x, y0 + y, 1.3, 0.95)
    # o R1 (0805, 10k) em pe
    E.passivo(d, rx, ry, '0805', 'R', '103', vertical=True)
    # a barra de tres pinos a 90 graus, deitada na borda direita
    xb = x0 + 18.5
    E.sombra(d, xb - 2.5, min(ys_barra) - 1.27, 2.5, max(ys_barra) - min(ys_barra) + 2.54, rx=0.15)
    d.ret(xb - 2.5, min(ys_barra) - 1.27, 2.5, max(ys_barra) - min(ys_barra) + 2.54, 'url(#plastico)', rx=0.15,
          stroke='#0b0c0d', sw=0.1)
    for y in ys_barra:
        d.ret(xb - 1.0, y - 0.32, comp_pinos + 1.0, 0.64, 'url(#metal)', stroke='#80868d', sw=0.05)
        d.ret(xb + comp_pinos - 0.5, y - 0.32, 0.5, 0.64, '#e9ecef')
        d.linha(xb - 2.2, y, xb - 0.3, y, '#2c2e32', 0.25)


def perna(d, x1, y1, x2, y2):
    """A perna de um componente de furo, do corpo ate a ilha (com a solda)."""
    d.linha(x1, y1, x2, y2, '#7d848b', 0.5)
    d.linha(x1, y1, x2, y2, '#d3d8dd', 0.32)
    d.circ(x2, y2, 0.5, ESTANHO)
    d.circ(x2 - 0.1, y2 - 0.1, 0.18, '#ffffff', op=0.7)
