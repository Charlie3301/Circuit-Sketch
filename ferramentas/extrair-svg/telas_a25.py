"""O acabamento comum das telas (OLED, TFT e LCD) no padrao da Alpha 25.

As fotos de frente dos modulos mostram o que os desenhos antigos nao tinham:
a placa com os furos de fixacao vazados (o anel de estanho em volta), a sombra
do painel (o vidro fica 1,5 a 7 mm acima da placa), a solda de cada pino da
barra (o cone brilhante que reflete o escuro do estudio, com a ponta cortada
do pino no meio), e o flex do vidro dobrado para tras (o preto de trama do
OLED, com as fitas amarelas nas pontas). Tudo em milimetros, num Desenho do
placas-arduino.py.
"""
import importlib.util
import os

AQUI = os.path.dirname(os.path.abspath(__file__))
_spec = importlib.util.spec_from_file_location('estilo_a25', os.path.join(AQUI, 'estilo_a25.py'))
E = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(E)

A = E.A
f = E.f
ESTANHO = E.ESTANHO

DEFS = '''  <radialGradient id="solda" cx="0.36" cy="0.32" r="0.78"><stop offset="0" stop-color="#ffffff" stop-opacity="0.55"/><stop offset="0.38" stop-color="#ffffff" stop-opacity="0.06"/><stop offset="1" stop-color="#000000" stop-opacity="0.35"/></radialGradient>
  <linearGradient id="vidro-borda" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#9fb0bf"/><stop offset="0.5" stop-color="#4d5966"/><stop offset="1" stop-color="#8996a3"/></linearGradient>
  <pattern id="trama" x="0" y="0" width="2.4" height="2.4" patternUnits="userSpaceOnUse" patternTransform="rotate(40)"><rect width="2.4" height="2.4" fill="#1e1f23"/><rect width="2.4" height="1.1" fill="#2b2c31"/></pattern>
'''


def retangulo(x, y, w, h, r):
    return [('M', x + r, y), ('L', x + w - r, y), ('A', r, 0, 1, x + w, y + r), ('L', x + w, y + h - r),
            ('A', r, 0, 1, x + w - r, y + h), ('L', x + r, y + h), ('A', r, 0, 1, x, y + h - r),
            ('L', x, y + r), ('A', r, 0, 1, x + r, y), ('Z',)]


def placa(d, x, y, w, h, rx, cor, borda, furos=(), sombra=0.3):
    """A placa com a sombra e os furos de fixacao vazados. furos: (cx, cy,
    diametro do furo, diametro do anel); o anel e de estanho (0 = sem anel)."""
    if sombra:
        E.sombra(d, x, y, w, h, rx=rx, op=sombra)
    cmds = retangulo(x, y, w, h, rx)
    for cx, cy, fd, _ in furos:
        r = fd / 2
        cmds += [('M', cx - r, cy), ('A', r, 1, 0, cx + r, cy), ('A', r, 1, 0, cx - r, cy), ('Z',)]
    for fill, stroke in ((cor, borda), ('url(#luz)', None)):
        d.caminho(cmds, fill, stroke, 0.15 if stroke else 0.0)
        d.partes[-1] = d.partes[-1].replace('<path ', '<path fill-rule="evenodd" ', 1)
    for cx, cy, fd, ad in furos:
        if ad > fd:
            larg = (ad - fd) / 2
            d.circ(cx, cy, fd / 2 + larg / 2, None, ESTANHO, larg)
            d.circ(cx - 0.12, cy - 0.12, fd / 2 + larg / 2, None, '#ffffff', larg * 0.35)
            d.partes[-1] = d.partes[-1].replace('<circle ', '<circle stroke-opacity="0.35" ', 1)
        d.circ(cx, cy, fd / 2, None, '#000000', 0.12)


def solda(d, x, y, quadrado=False):
    """O pino da barra soldado por tras: a ilha de estanho, o cone de solda
    brilhante e a ponta cortada do pino."""
    if quadrado:
        d.ret_c(x, y, 1.75, 1.75, ESTANHO, rx=0.1)
    else:
        d.circ(x, y, 0.88, ESTANHO)
    d.circ(x, y, 0.72, '#59626c')
    d.circ(x, y, 0.72, 'url(#solda)')
    d.ret_c(x, y, 0.5, 0.5, '#c4cad0', rx=0.05)
    d.ret_c(x - 0.07, y - 0.07, 0.24, 0.24, '#f4f6f8', op=0.8)


def sombra_do_painel(d, x0, y0, x1, y1, rx=0.3, alto=1.0, op=0.38):
    """A sombra do painel na placa (alto: o quanto o vidro fica acima dela)."""
    d.ret(x0 + 0.2 * alto, y0 + 0.3 * alto, x1 - x0, y1 - y0, '#000000', rx=rx, op=op)


def flex_oled(d, x0, x1, y0, y1, fitas=True):
    """O flex preto do OLED dobrado na borda (a trama do reforco) e as fitas
    amarelas das pontas."""
    d.ret(x0, y0, x1 - x0, y1 - y0, 'url(#trama)', rx=0.45, stroke='#0d0d0f', sw=0.1)
    d.ret(x0 + 0.3, y1 - 0.9, x1 - x0 - 0.6, 0.5, '#ffffff', rx=0.2, op=0.07)
    if fitas:
        for xa in (x0 - 1.9, x1 - 0.1):
            d.ret(xa, y1 - 2.4, 2.0, 1.3, '#e9c21c', rx=0.25, stroke='#b48f0c', sw=0.08)
            d.ret(xa + 0.1, y1 - 2.35, 1.8, 0.35, '#fff3a8', rx=0.15, op=0.6)
