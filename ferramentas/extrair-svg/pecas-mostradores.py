"""python ferramentas/extrair-svg/pecas-mostradores.py

As saidas novas da Alpha 25, no estilo das placas e no tamanho real (as
medidas dos fornecedores):

  - tm1637          : o HW-069 pela foto de frente (girada: a barra na
                      direita): a placa azul de 42 x 24 mm com os quatro furos
                      de 2,6 mm sem anel, o display de 0,36" (a caixa branca e
                      a face preta com os segmentos apagados claros, os digitos
                      no passo de ~7,7 mm e os dois pontos), o J1 a 90 graus
                      com os pinos para fora, a trilha e as vias da borda, o
                      U1 e o "4-Digit Display V1.2". A cor do display e o
                      modelo da peca.
  - max7219-matriz  : o kit azul montado (a placa de 32 x 32 mm com o
                      MAX7219CWG fica embaixo da matriz): de cima, a matriz
                      1088AS de 32 x 32 mm (os pontos de 3 mm no passo de
                      4 mm, cinza quando apagados, como na foto) e as duas
                      barras a 90 graus saindo por baixo dela -- a de entrada
                      (VCC, GND, DIN, CS, CLK) e a de saida (VCC, GND, DOUT,
                      CS, CLK) para encadear outra.
  - modulo-semaforo : o semaforo pela foto de cima: a placa preta em T de
                      55 mm (a barra de 13 mm com os LEDs de 8 mm, o pescoco
                      de 6,3 mm e a base de 21 mm com os dois furos e a barra
                      G, Y, R, GND a 90 graus na ponta), os resistores 0805 de
                      330, 220 e 470 ohms ao lado de cada LED.

O que acende -- os segmentos do TM1637, os pontos da matriz, os LEDs do
semaforo -- sai em px da caixa para app/DesenhoDosMostradores.cs; o editor
pinta por cima.
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

_spec_e = importlib.util.spec_from_file_location('estilo_a25', os.path.join(AQUI, 'estilo_a25.py'))
E = importlib.util.module_from_spec(_spec_e)
_spec_e.loader.exec_module(E)

Desenho, T, f = pa.Desenho, pa.T, pa.f
ESTANHO, SERIGRAFIA = pa.ESTANHO, pa.SERIGRAFIA
A = 96 / 25.4
PASSO = 2.54
AZUL, AZUL_BORDA, BRANCO = '#1d4fa6', '#0f2c66', '#f3f3f1'

DEFS_EXTRA = '''  <radialGradient id="difusor" cx="0.42" cy="0.38" r="0.62"><stop offset="0" stop-color="#ffffff" stop-opacity="0.22"/><stop offset="0.65" stop-color="#ffffff" stop-opacity="0"/><stop offset="1" stop-color="#000000" stop-opacity="0.22"/></radialGradient>
'''

pecas = []
cs_segmentos, cs_pontos, cs_luzes = [], [], []


def svg_de(d, L, H):
    w, h = L * A, H * A
    return (f'<svg xmlns="http://www.w3.org/2000/svg" version="1.1" width="{f(w)}px" height="{f(h)}px" '
            f'viewBox="0 0 {f(w)} {f(h)}">\n<defs>\n{E.DEFS}{DEFS_EXTRA}</defs>\n' + ''.join(d.partes) + '</svg>\n')


def gravar(nome, svg):
    io.open(pa.BASE + f'app/assets/{nome}.svg', 'w', encoding='utf-8', newline='').write(svg)


def guardar(id_peca, L, H, pinos):
    pecas.append({'id': id_peca, 'largura': round(L * A), 'altura': round(H * A),
                  'pinos': [{'nome': n, 'x': round(x * A, 2), 'y': round(y * A, 2), 'sinais': s, 'descricao': dsc}
                            for n, x, y, s, dsc in pinos],
                  'variantes': [], 'metades': False})
    print(f'{id_peca}: {round(L * A)}x{round(H * A)} px, {len(pinos)} pinos')


def furo(d, x, y, furo_d, anel_d, fundo='#0d1a2e'):
    d.circ(x, y, anel_d / 2, ESTANHO)
    d.circ(x, y, furo_d / 2, fundo)


def pino_soldado(d, x, y, quadrado=False):
    if quadrado:
        d.ret_c(x, y, 1.7, 1.7, ESTANHO)
    else:
        d.circ(x, y, 0.85, ESTANHO)
    d.circ(x, y, 0.55, '#dfe3e7')
    d.circ(x - 0.15, y - 0.15, 0.2, '#ffffff', op=0.7)


def segmentos_do_digito(cx, cy, w, h, t, inclinacao=8.0):
    """Os sete segmentos de um digito inclinado (os hexagonos de A a G), em mm."""
    k = math.tan(math.radians(inclinacao))
    sx = lambda x, y: (cx + x - (y - cy) * k, y)                   # inclina para a direita
    meio, topo, base = cy, cy - h / 2, cy + h / 2
    x0, x1 = cx - w / 2, cx + w / 2
    g = t * 0.12

    def horizontal(y):
        return [sx(x0 - cx + t * 0.5 + g, y), sx(x0 - cx + t + g, y - t / 2), sx(x1 - cx - t - g, y - t / 2),
                sx(x1 - cx - t * 0.5 - g, y), sx(x1 - cx - t - g, y + t / 2), sx(x0 - cx + t + g, y + t / 2)]

    def vertical(x, y0, y1):
        return [sx(x - cx, y0 + g), sx(x - cx + t / 2, y0 + t / 2 + g), sx(x - cx + t / 2, y1 - t / 2 - g),
                sx(x - cx, y1 - g), sx(x - cx - t / 2, y1 - t / 2 - g), sx(x - cx - t / 2, y0 + t / 2 + g)]

    xe, xd = x0 + t / 2, x1 - t / 2
    return {'A': horizontal(topo + t / 2), 'G': horizontal(meio), 'D': horizontal(base - t / 2),
            'F': vertical(xe, topo + t / 2, meio), 'B': vertical(xd, topo + t / 2, meio),
            'E': vertical(xe, meio, base - t / 2), 'C': vertical(xd, meio, base - t / 2)}


def circulo(cx, cy, r, n=14):
    return [(cx + r * math.cos(2 * math.pi * k / n), cy + r * math.sin(2 * math.pi * k / n)) for k in range(n)]


def em_px(pontos):
    return '[' + ', '.join(f'({x * A:.2f}, {y * A:.2f})' for x, y in pontos) + ']'


def contorno_com_furos(d, cmds, furos, cor, borda, sw=0.15):
    """A placa (o caminho cmds) com os furos (cx, cy, r) vazados, sem anel: a
    parede do furo e so um fio mais escuro."""
    for cx, cy, r in furos:
        cmds = cmds + [('M', cx - r, cy), ('A', r, 1, 0, cx + r, cy), ('A', r, 1, 0, cx - r, cy), ('Z',)]
    for fill, stroke in ((cor, borda), ('url(#luz)', None)):
        d.caminho(cmds, fill, stroke, sw if stroke else 0.0)
        d.partes[-1] = d.partes[-1].replace('<path ', '<path fill-rule="evenodd" ', 1)
    for cx, cy, r in furos:
        d.circ(cx, cy, r, None, borda, 0.15)


def retangulo(x, y, w, h, r):
    return [('M', x + r, y), ('L', x + w - r, y), ('A', r, 0, 1, x + w, y + r), ('L', x + w, y + h - r),
            ('A', r, 0, 1, x + w - r, y + h), ('L', x + r, y + h), ('A', r, 0, 1, x, y + h - r),
            ('L', x, y + r), ('A', r, 0, 1, x + r, y), ('Z',)]


def texto_girado(d, x, y, t, tam, cor, graus, peso='bold'):
    """Texto centrado em (x, y), girado de verdade (o d.texto deixa sempre legivel)."""
    X, Y = d.t(x, y)
    e = d.t.escala
    d.partes.append(f'<text transform="translate({f(X)} {f(Y)}) rotate({graus})" x="0" y="{f(tam * 0.45 * e)}" '
                    f'font-family="Arial" font-weight="{peso}" font-size="{f(tam * 1.25 * e)}" fill="{cor}" '
                    f'text-anchor="middle">{t}</text>\n')


def pinos_90(d, ys, x_plastico, x_ponta, para_direita=True):
    """Os pinos de uma barra a 90 graus, deitados no plano e saindo da placa
    (a ponta clara de estanho)."""
    for y in ys:
        x0, x1 = (x_plastico, x_ponta) if para_direita else (x_ponta, x_plastico)
        d.ret(x0 + 0.12, y - 0.32 + 0.18, x1 - x0, 0.64, '#000000', op=0.3)          # a sombra no que esta embaixo
        d.ret(x0, y - 0.32, x1 - x0, 0.64, 'url(#metal)', stroke='#80868d', sw=0.05)
        d.ret(x_ponta - (0.5 if para_direita else 0.0), y - 0.32, 0.5, 0.64, '#e9ecef')


# =====================================================================
#  TM1637 de 4 digitos com os dois pontos (o HW-069)
# =====================================================================
# A foto de frente do HW-069 (a placa em pe, o J1 em cima) girada 90 graus no
# sentido horario: X = (1232 - v) / 26,83 e Y = (u - 34) / 26,4 (os px da foto
# medidos nas bordas da placa de 42 x 24 mm). O pino continua onde estava (X =
# 40, no meio do plastico do J1); a caixa cresce para a direita, para os pinos
# a 90 graus sairem da placa como na foto.
L_PLACA_TM = 159 / A                                         # 42,07 x 24,08 mm: a placa
L, H = E.caixa(47.6, 91 / A)
PINOS_TM = [('CLK', [], 'O relogio do TM1637 (qualquer pino digital: o CLK do TM1637Display)'),
            ('DIO', [], 'Os dados do TM1637 (qualquer pino digital: o DIO do TM1637Display)'),
            ('VCC', ['power'], 'Alimentacao: 3,3 a 5 V'), ('GND', ['power'], 'GND')]
X_TM = 40.0
YS_TM = [H / 2 + (k - 1.5) * PASSO for k in range(4)]
MASCARA_TM, BORDA_TM, COBRE_TM = '#155686', '#0b3456', '#1b4769'
CORPO_TM = (5.85, 3.56, 38.8, 19.36)                        # a caixa branca do display
FACE_TM = (6.7, 4.2, 38.0, 18.7)                             # a face preta
CENTROS = [10.62, 18.19, 25.98, 33.73]                       # os digitos (o passo de ~7,7 mm da foto)
CY_TM = 11.6
DOIS_PONTOS = [(22.36, 8.86), (21.88, 14.09)]
segs_tm = {}
d = Desenho(T(A, 0, 0, A, 0, 0))
E.sombra(d, 0, 0, L_PLACA_TM, H, rx=0.8, op=0.3)
FUROS_TM = [(x, y, 1.3) for x in (1.95, 40.1) for y in (1.95, H - 1.95)]
contorno_com_furos(d, retangulo(0, 0, L_PLACA_TM, H, 0.8), FUROS_TM, MASCARA_TM, BORDA_TM)
# a trilha da borda de cima (do U1 para a via do lado do digito 1) e as vias
d.poli([(32.05, 3.45), (31.57, 2.69), (11.44, 2.69), (11.26, 2.88)], None, COBRE_TM, 0.32, fechado=False, cap='round')
for x, y, r in ((11.26, 2.88, 0.42), (25.72, 1.82, 0.22), (16.59, 1.74, 0.22), (4.36, 21.3, 0.22),
                (35.6, 21.5, 0.22), (0.63, 5.34, 0.22)):
    d.circ(x, y, r, COBRE_TM)
    d.circ(x, y, r * 0.45, '#08223a')
for y in (9.09, 10.45):                                       # as duas ilhas de teste embaixo do digito 1
    d.linha(4.73, y, 6.2, y, COBRE_TM, 0.5)
    d.circ(4.73, y, 0.5, COBRE_TM)
    d.circ(4.73, y, 0.22, '#08223a')
S = SERIGRAFIA
d.texto(22.1, 3.05, 'U1', 1.0, S, alinhar='center', peso='normal')
d.texto(39.9, 5.55, 'J1', 1.4, S, alinhar='center', peso='normal')
d.texto(21.3, 22.15, '4-Digit Display V1.2', 1.5, S, alinhar='center', peso='normal')
# o display: a caixa branca (com o filme), a face preta e os segmentos apagados
x0, y0, x1, y1 = CORPO_TM
d.ret(x0 + 0.25, y0 + 0.35, x1 - x0, y1 - y0, '#000000', rx=0.3, op=0.35)
d.ret(x0, y0, x1 - x0, y1 - y0, '#dfe2e6', rx=0.3, stroke='#a6acb3', sw=0.1)
d.ret(x0, y0, x1 - x0, y1 - y0, 'url(#luz)', rx=0.3)
x0, y0, x1, y1 = FACE_TM
d.ret(x0, y0, x1 - x0, y1 - y0, '#262729', rx=0.25, stroke='#121314', sw=0.08)
for k, cx in enumerate(CENTROS):
    for nome, poli in segmentos_do_digito(cx, CY_TM, 5.0, 9.64, 0.8, 5.0).items():
        d.poli(poli, '#d6d2cb', '#aaa59d', 0.04)                  # o segmento apagado (o difusor claro)
        segs_tm[f'D{k + 1}{nome}'] = poli
for x, y in DOIS_PONTOS:
    d.circ(x, y, 0.6, '#d6d2cb', '#aaa59d', 0.04)
segs_tm['COLON'] = circulo(*DOIS_PONTOS[0], 0.6, 10) + circulo(*DOIS_PONTOS[1], 0.6, 10)[::-1]
# o J1 a 90 graus: o plastico em cima da placa e os pinos para fora
E.sombra(d, 38.75, YS_TM[0] - 1.27, 2.5, 4 * PASSO, rx=0.15, op=0.35)
d.ret(38.75, YS_TM[0] - 1.27, 2.5, 4 * PASSO, 'url(#plastico)', rx=0.15, stroke='#0b0c0d', sw=0.1)
pinos_90(d, YS_TM, 39.3, L - 0.25)
for y in YS_TM:
    d.ret_c(39.6, y, 0.5, 0.64, '#1c1d20')                         # o furo do pino no plastico
gravar('tm1637', svg_de(d, L, H))
guardar('tm1637', L, H, [(n, X_TM, YS_TM[k], s, dsc) for k, (n, s, dsc) in enumerate(PINOS_TM)])
cs_segmentos.append(('tm1637', segs_tm))


# =====================================================================
#  matriz 8 x 8 do MAX7219 (o kit azul montado)
# =====================================================================
L, H = 50.0, 32.0
ENTRADA = [('VCC', ['power'], 'Alimentacao: 5 V (a matriz toda acesa puxa ~300 mA)'), ('GND', ['power'], 'GND'),
           ('DIN', [], 'Os dados (o DIN da LedControl)'), ('CS', [], 'O chip select (o CS da LedControl)'),
           ('CLK', [], 'O relogio (o CLK da LedControl)')]
SAIDA = [('VCC.2', ['power'], 'O VCC, para a proxima matriz'), ('GND.2', ['power'], 'O GND, para a proxima matriz'),
         ('DOUT', [], 'Os dados para o DIN da proxima matriz (a cadeia da LedControl)'),
         ('CS.2', [], 'O CS, para a proxima matriz'), ('CLK.2', [], 'O CLK, para a proxima matriz')]
YS_MX = [H / 2 + (k - 2) * PASSO for k in range(5)]
X_ENT, X_SAI = 2.4, L - 2.4
MX0, MY0 = 9.0, 0.0
centros = [(MX0 + 2.0 + 4.0 * c, MY0 + 2.0 + 4.0 * r) for r in range(8) for c in range(8)]
d = Desenho(T(A, 0, 0, A, 0, 0))
# A placa de 32 x 32 mm fica toda embaixo da matriz; de cima, so as barras a 90
# graus aparecem: o plastico saindo 1,4 mm da borda e os pinos de 6 mm.
for lado, (x_pl, x_ponta) in ((-1, (MX0 - 1.4, 1.9)), (1, (MX0 + 32.0 + 1.4, L - 1.9))):
    xa = x_pl if lado < 0 else x_pl - 2.5
    d.ret(xa + 0.12, YS_MX[0] - 1.27 + 0.18, 2.5, 5 * PASSO, '#000000', rx=0.15, op=0.3)
    d.ret(xa, YS_MX[0] - 1.27, 2.5, 5 * PASSO, 'url(#plastico)', rx=0.15, stroke='#0b0c0d', sw=0.1)
    pinos_90(d, YS_MX, x_pl + 0.3 * lado, x_ponta, para_direita=lado > 0)
# a matriz 1088AS: a sombra (ela fica alta, em cima dos soquetes), a face e os pontos
d.ret(MX0 + 0.5, MY0 + 0.7, 32.0, 32.0, '#000000', rx=0.3, op=0.35)
d.ret(MX0, MY0, 32.0, 32.0, '#2c2727', rx=0.3, stroke='#120f0f', sw=0.15)
d.ret(MX0, MY0, 32.0, 32.0, 'url(#luz)', rx=0.3)
for x, y in centros:
    d.circ(x, y, 1.5, '#9d978a', '#6c675e', 0.08)
    d.circ(x, y, 1.5, 'url(#difusor)')
gravar('max7219-matriz', svg_de(d, L, H))
guardar('max7219-matriz', L, H, [(n, X_ENT, YS_MX[k], s, dsc) for k, (n, s, dsc) in enumerate(ENTRADA)]
        + [(n, X_SAI, YS_MX[k], s, dsc) for k, (n, s, dsc) in enumerate(SAIDA)])
cs_pontos.append(('max7219-matriz', centros, 1.5))


# =====================================================================
#  o semaforo (a placa preta em T)
# =====================================================================
# A foto de cima (a base na direita, girada 90 graus no sentido horario): a
# barra de 13 x 35 mm, o pescoco de 6,3 x 12,2 mm e a base de 21 x 7,8 mm -- os
# 55 mm da ficha. A barra G, Y, R, GND a 90 graus sai da ponta da base.
L, H = E.caixa(21.2, 231 / A)
CX = L / 2
PINOS_SEM = [('G', [], 'O LED verde (com o resistor de 470 ohms dentro): HIGH acende'),
             ('Y', [], 'O LED amarelo (com o resistor de 220 ohms dentro): HIGH acende'),
             ('R', [], 'O LED vermelho (com o resistor de 330 ohms dentro): HIGH acende'),
             ('GND', ['power'], 'GND (o catodo comum dos tres)')]
XS_SEM = [CX + (k - 1.5) * PASSO for k in range(4)]
Y_PONTA = H - 0.8
Y_BARRA, Y_PESCOCO, Y_BASE = 35.0, 47.2, 55.0
#        pino, centro, cor do LED (o difusor), a luz,    o resistor
LEDS = [('R', 4.75, '#d1281f', '#c8261e', '331'), ('Y', 15.95, '#e4d83c', '#e5b81d', '221'),
        ('G', 27.15, '#2fae4c', '#2fa046', '471')]
d = Desenho(T(A, 0, 0, A, 0, 0))


def contorno_t(a):
    r, rf, rb = 1.0, 1.0, 0.8
    return [('M', a - 6.5 + r, 0), ('L', a + 6.5 - r, 0), ('A', r, 0, 1, a + 6.5, r),
            ('L', a + 6.5, Y_BARRA - r), ('A', r, 0, 1, a + 6.5 - r, Y_BARRA),
            ('L', a + 3.15 + rf, Y_BARRA), ('A', rf, 0, 0, a + 3.15, Y_BARRA + rf),
            ('L', a + 3.15, Y_PESCOCO - rf), ('A', rf, 0, 0, a + 3.15 + rf, Y_PESCOCO),
            ('L', a + 10.5 - rb, Y_PESCOCO), ('A', rb, 0, 1, a + 10.5, Y_PESCOCO + rb),
            ('L', a + 10.5, Y_BASE - rb), ('A', rb, 0, 1, a + 10.5 - rb, Y_BASE),
            ('L', a - 10.5 + rb, Y_BASE), ('A', rb, 0, 1, a - 10.5, Y_BASE - rb),
            ('L', a - 10.5, Y_PESCOCO + rb), ('A', rb, 0, 1, a - 10.5 + rb, Y_PESCOCO),
            ('L', a - 3.15 - rf, Y_PESCOCO), ('A', rf, 0, 0, a - 3.15, Y_PESCOCO - rf),
            ('L', a - 3.15, Y_BARRA + rf), ('A', rf, 0, 0, a - 3.15 - rf, Y_BARRA),
            ('L', a - 6.5 + r, Y_BARRA), ('A', r, 0, 1, a - 6.5, Y_BARRA - r),
            ('L', a - 6.5, r), ('A', r, 0, 1, a - 6.5 + r, 0), ('Z',)]


pinos_desce = [x for x in XS_SEM]
for x in pinos_desce:                                         # os pinos da barra, para fora da base
    d.ret(x - 0.32 + 0.12, Y_BASE - 1.5 + 0.18, 0.64, Y_PONTA + 0.6 - Y_BASE + 1.5, '#000000', op=0.3)
d.caminho([(c[0], *c[1:-2], c[-2] + 0.15, c[-1] + 0.25) if c[0] != 'Z' else c for c in contorno_t(CX)], '#000000', op=0.3)
contorno_com_furos(d, contorno_t(CX), [(CX + s_ * 8.4, 49.4, 1.3) for s_ in (-1, 1)], '#151619', '#050506')
S = SERIGRAFIA
for k, x in enumerate(XS_SEM):
    texto_girado(d, x, 50.6, PINOS_SEM[k][0], 1.15, S, 90)
texto_girado(d, CX, 41.1, 'PCB0105', 1.05, S, 90, peso='normal')
for _, y, _, _, cod in LEDS:                                    # os resistores 0805 ao lado de cada LED
    y_r = y + 5.6
    d.ret_c(CX, y_r, 2.9, 1.9, None, rx=0.15, stroke=S, sw=0.12)
    E.passivo(d, CX, y_r, '0805', 'R', cod)
for nome, y, cor, _, _ in LEDS:                               # os LEDs de 8 mm: o colar, a cupula difusa
    d.circ(CX + 0.3, y + 0.45, 4.5, '#000000', op=0.35)
    d.circ(CX, y, 4.5, cor, stroke='#0b0b0c', sw=0.1)
    d.circ(CX, y, 4.5, '#000000', op=0.18)
    d.circ(CX, y, 4.0, cor)
    d.circ(CX, y, 4.0, 'url(#cupula)')
    d.circ(CX, y, 1.1, '#000000', op=0.12)                     # o chip, por dentro
# a barra a 90 graus na ponta da base: o plastico e os pinos para baixo
d.ret(XS_SEM[0] - 1.27, Y_BASE - 2.4, 4 * PASSO, 2.4, 'url(#plastico)', rx=0.15, stroke='#0b0c0d', sw=0.1)
for x in XS_SEM:
    d.ret(x - 0.32, Y_BASE - 1.6, 0.64, Y_PONTA + 0.6 - Y_BASE + 1.6, 'url(#metal-h)', stroke='#80868d', sw=0.05)
    d.ret(x - 0.32, Y_PONTA + 0.1, 0.64, 0.5, '#e9ecef')
    d.ret_c(x, Y_BASE - 1.9, 0.64, 0.5, '#1c1d20')
gravar('modulo-semaforo', svg_de(d, L, H))
guardar('modulo-semaforo', L, H, [(n, XS_SEM[k], Y_PONTA, s, dsc) for k, (n, s, dsc) in enumerate(PINOS_SEM)])
cs_luzes.append(('modulo-semaforo', [(CX, y, 4.0, luz, nome) for nome, y, _, luz, _ in LEDS]))


cam = pa.BASE + 'app/assets/pecas.json'
lista = json.load(io.open(cam, encoding='utf-8'))
novos = {p['id'] for p in pecas}
lista = [p for p in lista if p['id'] not in novos] + pecas
lista.sort(key=lambda p: p['id'])
io.open(cam, 'w', encoding='utf-8', newline='').write(json.dumps(lista, ensure_ascii=False, indent=1))
print(f'catalogo: {len(lista)} pecas')

linhas = ['namespace Simulador.App;', '', '// GERADO por ferramentas/extrair-svg/pecas-mostradores.py -- nao edite a mao.', '//',
          '// O que acende nas saidas novas (px da caixa): os segmentos do TM1637, os',
          '// pontos da matriz do MAX7219 e os LEDs do semaforo.',
          'static class DesenhoDosMostradores', '{',
          '    /// <summary>O contorno de cada segmento (D1A ... D4G e os dois pontos, o COLON).</summary>',
          '    public static readonly Dictionary<string, Dictionary<string, (double X, double Y)[]>> Segmentos = new()', '    {']
for tipo, segs in cs_segmentos:
    linhas.append(f'        ["{tipo}"] = new()')
    linhas.append('        {')
    for nome, poli in segs.items():
        linhas.append(f'            ["{nome}"] = {em_px(poli)},')
    linhas.append('        },')
linhas += ['    };', '', '    /// <summary>O centro de cada ponto da matriz (linha a linha, de cima) e o raio.</summary>',
           '    public static readonly Dictionary<string, ((double X, double Y)[] Centros, double Raio)> Pontos = new()', '    {']
for tipo, centros, r in cs_pontos:
    linhas.append(f'        ["{tipo}"] = ({em_px(centros)}, {r * A:.2f}),')
linhas += ['    };', '', '    /// <summary>Os LEDs (o centro, o raio, a cor e o pino que acende cada um).</summary>',
           '    public static readonly Dictionary<string, (double X, double Y, double Raio, string Cor, string Pino)[]> Luzes = new()', '    {']
for tipo, luzes in cs_luzes:
    itens = ', '.join(f'({x * A:.2f}, {y * A:.2f}, {r * A:.2f}, "{cor}", "{nome}")' for x, y, r, cor, nome in luzes)
    linhas.append(f'        ["{tipo}"] = [{itens}],')
linhas += ['    };', '}']
io.open(pa.BASE + 'app/DesenhoDosMostradores.cs', 'w', encoding='utf-8', newline='').write('\n'.join(linhas) + '\n')
print('  app/DesenhoDosMostradores.cs')
