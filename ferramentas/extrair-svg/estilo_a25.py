"""O padrao de qualidade dos desenhos da Alpha 25.

As placas feitas pelos CADs oficiais (Arduino, ESP32, Pico, STM32, DS1307,
PCM5102A, nRF24L01) tem um acabamento que as feitas a mao perderam: trilhas
por baixo da mascara, vias, ilhas com anel, serigrafia com o contorno dos
componentes e o valor, componentes com marcacao, e a placa cheia como a de
verdade. Este modulo junta essas primitivas para todo desenho novo sair igual:

  - a placa (a mascara com borda, a luz, os furos de fixacao com anel);
  - as trilhas e as vias (um tom mais claro que a mascara);
  - as ilhas: de furo (anel de ouro ou de estanho, o pino 1 quadrado), SMD e
    as casteladas (o meio furo na borda dos modulos);
  - os componentes: o 0402/0603/0805 (resistor com o codigo, capacitor,
    indutor), o LED SMD, o SOT-23, o SOIC, o QFN, o cristal, a antena de
    trilha, a ceramica, o conector U.FL, a blindagem com a gravacao;
  - a serigrafia (texto, contorno, linha), sempre no mesmo branco.

Tudo em milimetros, num Desenho do placas-arduino.py.
"""
import importlib.util
import math
import os

AQUI = os.path.dirname(os.path.abspath(__file__))
_spec = importlib.util.spec_from_file_location('placas_arduino', os.path.join(AQUI, 'placas-arduino.py'))
pa = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(pa)

Desenho, T, f, mover, rodar = pa.Desenho, pa.T, pa.f, pa.mover, pa.rodar
A = 96 / 25.4
OURO, ESTANHO, FURO = pa.OURO, pa.ESTANHO, pa.FURO
SERIGRAFIA = pa.SERIGRAFIA
PRETO_CI, MARCA_CI = pa.PRETO_CI, pa.MARCA_CI

# a mascara de cada cor de placa: (mascara, borda, trilha por baixo)
MASCARAS = {
    'azul': ('#1d55a8', '#123a78', '#2d6cc6'),
    'azul-esp': ('#1b4f9a', '#10356e', '#2a64b6'),
    'preta': ('#1b1d21', '#0a0b0d', '#2a2e35'),
    'vermelha': ('#ad2328', '#6c1216', '#c4373c'),
    'verde': ('#1d7a3d', '#114c25', '#2c9050'),
    'roxa': ('#5a2a8a', '#3a1a5a', '#6e3aa0'),
}

DEFS = pa.DEFS + '''  <linearGradient id="blindagem" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#eef1f3"/><stop offset="0.45" stop-color="#c8cdd2"/><stop offset="1" stop-color="#9aa1a8"/></linearGradient>
  <radialGradient id="cupula" cx="0.36" cy="0.3" r="0.78"><stop offset="0" stop-color="#ffffff" stop-opacity="0.6"/><stop offset="0.32" stop-color="#ffffff" stop-opacity="0.1"/><stop offset="1" stop-color="#000000" stop-opacity="0.32"/></radialGradient>
  <linearGradient id="ceramica" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#f4f1e8"/><stop offset="1" stop-color="#cfc9b8"/></linearGradient>
'''


def svg(d, L, H):
    """O SVG da caixa L x H (mm), com o desenho d."""
    w, h = L * A, H * A
    return (f'<svg xmlns="http://www.w3.org/2000/svg" version="1.1" width="{f(w)}px" height="{f(h)}px" '
            f'viewBox="0 0 {f(w)} {f(h)}">\n<defs>\n{DEFS}</defs>\n' + ''.join(d.partes) + '</svg>\n')


def novo():
    return Desenho(T(A, 0, 0, A, 0, 0))


def caixa(lmm, hmm):
    """A caixa em px inteiros, de volta em mm (o enquadramento do SVG pede)."""
    return round(lmm * A) / A, round(hmm * A) / A


# =====================================================================
#  a placa, as trilhas e as vias
# =====================================================================
class Placa:
    def __init__(self, d, cor):
        self.d = d
        self.mascara, self.borda, self.trilha_cor = MASCARAS[cor]

    def contorno(self, x, y, w, h, rx=0.5, furos=(), cortes=()):
        """A mascara com os furos de fixacao vazados (e o anel de cada um).
        cortes: retangulos (x, y, w, h) tirados da placa (o recorte da antena)."""
        d = self.d
        cmds = [('M', x + rx, y), ('L', x + w - rx, y), ('A', rx, 0, 1, x + w, y + rx), ('L', x + w, y + h - rx),
                ('A', rx, 0, 1, x + w - rx, y + h), ('L', x + rx, y + h), ('A', rx, 0, 1, x, y + h - rx),
                ('L', x, y + rx), ('A', rx, 0, 1, x + rx, y), ('Z',)]
        for cx, cy, r in furos:
            cmds += [('M', cx - r, cy), ('A', r, 1, 0, cx + r, cy), ('A', r, 1, 0, cx - r, cy), ('Z',)]
        d.caminho(cmds, self.mascara, self.borda, 0.2)
        d.partes[-1] = d.partes[-1].replace('<path ', '<path fill-rule="evenodd" ', 1)
        d.caminho(cmds, 'url(#luz)')
        d.partes[-1] = d.partes[-1].replace('<path ', '<path fill-rule="evenodd" ', 1)
        for cx, cy, r in furos:
            d.circ(cx, cy, r + 0.35, None, OURO, 0.6)

    def trilha(self, pts, larg=0.25):
        """Uma trilha de cobre por baixo da mascara (um tom mais claro)."""
        self.d.poli(pts, None, stroke=self.trilha_cor, sw=larg, fechado=False, cap='round')

    def via(self, x, y, d_ext=0.6, d_furo=0.3):
        self.d.circ(x, y, d_ext / 2, self.trilha_cor)
        self.d.circ(x, y, d_furo / 2, self.borda)

    def area(self, x, y, w, h, rx=0.2):
        """Um plano de cobre (o GND) por baixo da mascara."""
        self.d.ret(x, y, w, h, self.trilha_cor, rx=rx, op=0.55)


# =====================================================================
#  as ilhas
# =====================================================================
def ilha_tht(d, x, y, furo=1.0, anel=1.7, quadrada=False, acabamento=OURO, fundo=FURO):
    if quadrada:
        d.ret_c(x, y, anel, anel, acabamento)
    else:
        d.circ(x, y, anel / 2, acabamento)
    d.circ(x, y, furo / 2, fundo)


def ilha_smd(d, x, y, w, h, acabamento=ESTANHO, rx=0.08):
    d.ret_c(x, y, w, h, acabamento, rx=rx)


def ilha_castelada(d, x, y, lado, comp=1.4, larg=1.0, furo=0.6, acabamento=OURO, fundo='#0c1016'):
    """O meio furo na borda do modulo: a ilha entra comp mm da borda (x, y e
    o ponto da borda) e o furo cortado ao meio fica na borda."""
    dx, dy = {'esq': (1, 0), 'dir': (-1, 0), 'cima': (0, 1), 'baixo': (0, -1)}[lado]
    cx, cy = x + dx * comp / 2, y + dy * comp / 2
    if dx:
        d.ret_c(cx, cy, comp, larg, acabamento, rx=0.12)
    else:
        d.ret_c(cx, cy, larg, comp, acabamento, rx=0.12)
    r = furo / 2
    # o meio circulo vazado, com a boca na borda
    if lado == 'esq':
        d.caminho([('M', x, y - r), ('A', r, 0, 1, x, y + r), ('Z',)], fundo)
    elif lado == 'dir':
        d.caminho([('M', x, y + r), ('A', r, 0, 1, x, y - r), ('Z',)], fundo)
    elif lado == 'cima':
        d.caminho([('M', x + r, y), ('A', r, 0, 1, x - r, y), ('Z',)], fundo)
    else:
        d.caminho([('M', x - r, y), ('A', r, 0, 1, x + r, y), ('Z',)], fundo)


# =====================================================================
#  a serigrafia
# =====================================================================
def silk_texto(d, x, y, t, tam=0.8, ang=0.0, alinhar='center', peso='bold'):
    d.texto(x, y, t, tam, SERIGRAFIA, ang=ang, alinhar=alinhar, peso=peso)


def silk_ret(d, x, y, w, h, larg=0.12, rx=0.0):
    d.ret(x, y, w, h, None, rx=rx, stroke=SERIGRAFIA, sw=larg)


def silk_linha(d, x1, y1, x2, y2, larg=0.12):
    d.linha(x1, y1, x2, y2, SERIGRAFIA, larg, cap='butt')


# =====================================================================
#  os componentes
# =====================================================================
TAMANHOS = {'0402': (1.0, 0.5), '0603': (1.6, 0.8), '0805': (2.0, 1.25), '1206': (3.2, 1.6)}


def sombra(d, x, y, w, h, rx=0.1, op=0.28):
    d.ret(x + 0.12, y + 0.16, w, h, '#000000', rx=rx, op=op)


def passivo(d, cx, cy, tam='0603', tipo='R', codigo=None, vertical=False, cor=None):
    """O resistor (preto, com o codigo), o capacitor (marrom-claro) ou o indutor
    (cinza) SMD, com os terminais de estanho."""
    c, l = TAMANHOS[tam]
    w, h = (l, c) if vertical else (c, l)
    corpo = cor or {'R': '#1c1c1e', 'C': '#b08a5c', 'L': '#3a3b3f', 'F': '#2a2b2e'}[tipo]
    sombra(d, cx - w / 2, cy - h / 2, w, h)
    d.ret_c(cx, cy, w, h, ESTANHO, rx=0.05)
    if vertical:
        d.ret_c(cx, cy, w, h * 0.6, corpo)
    else:
        d.ret_c(cx, cy, w * 0.6, h, corpo)
    d.ret_c(cx, cy, w, h, 'url(#luz)', rx=0.05)
    if codigo and tipo == 'R':
        d.texto(cx, cy, codigo, min(w, h) * 0.42, '#e8e8e8', ang=90 if vertical else 0, alinhar='center', peso='normal')


def led_smd(d, cx, cy, cor_luz, tam='0603', vertical=False):
    """O LED SMD apagado: a lente transparente com o chip da cor por baixo."""
    c, l = TAMANHOS[tam]
    w, h = (l, c) if vertical else (c, l)
    sombra(d, cx - w / 2, cy - h / 2, w, h)
    d.ret_c(cx, cy, w, h, ESTANHO, rx=0.05)
    if vertical:
        d.ret_c(cx, cy, w, h * 0.62, '#f2efe6')
    else:
        d.ret_c(cx, cy, w * 0.62, h, '#f2efe6')
    d.ret_c(cx, cy, min(w, h) * 0.45, min(w, h) * 0.45, cor_luz, op=0.75)
    d.ret_c(cx, cy, w, h, 'url(#luz)', rx=0.05)


def sot23(d, cx, cy, texto='', pinos=3, giro=0):
    d.entrar(mover(cx, cy) * rodar(giro))
    sombra(d, -1.45, -0.65, 2.9, 1.3)
    xs_baixo = [-0.95, 0.95] if pinos == 3 else [-0.95, 0, 0.95]
    xs_cima = [0] if pinos == 3 else [-0.95, 0.95]
    for x in xs_baixo:
        d.ret_c(x, 0.95, 0.45, 0.7, ESTANHO)
    for x in xs_cima:
        d.ret_c(x, -0.95, 0.45, 0.7, ESTANHO)
    d.ret_c(0, 0, 2.9, 1.3, PRETO_CI, rx=0.08)
    d.ret_c(0, 0, 2.9, 1.3, 'url(#luz)', rx=0.08)
    if texto:
        d.texto(0, 0, texto, 0.5, MARCA_CI, alinhar='center', peso='normal')
    d.sair()


def soic(d, cx, cy, n, linhas=(), vertical=False, passo=1.27, larg_corpo=3.9):
    """O SOIC de n pinos (o pino 1 em cima a esquerda, com a bolinha)."""
    por_lado = n // 2
    comp = (por_lado - 1) * passo + 1.0
    d.entrar(mover(cx, cy) * rodar(90 if vertical else 0))
    for k in range(por_lado):
        x = -comp / 2 + 0.5 + k * passo
        for s in (-1, 1):
            d.ret_c(x, s * (larg_corpo / 2 + 0.45), 0.42, 0.9, ESTANHO)
    sombra(d, -comp / 2, -larg_corpo / 2, comp, larg_corpo)
    d.ret_c(0, 0, comp, larg_corpo, PRETO_CI, rx=0.15)
    d.ret_c(0, 0, comp, larg_corpo, 'url(#luz)', rx=0.15)
    d.circ(-comp / 2 + 0.55, larg_corpo / 2 - 0.55, 0.28, '#3a3c41')
    for k, t in enumerate(linhas):
        d.texto(0, (k - (len(linhas) - 1) / 2) * 0.85, t, 0.62, MARCA_CI, alinhar='center', peso='normal')
    d.sair()


def qfn(d, cx, cy, lado, por_lado, linhas=(), passo=0.5):
    """O QFN quadrado (o ESP8266EX e 5 x 5 mm, 32 pinos), com a bolinha do pino 1."""
    meio = lado / 2
    for k in range(por_lado):
        o = (k - (por_lado - 1) / 2) * passo
        for s in (-1, 1):
            d.ret_c(cx + o, cy + s * (meio + 0.15), 0.25, 0.45, ESTANHO)
            d.ret_c(cx + s * (meio + 0.15), cy + o, 0.45, 0.25, ESTANHO)
    sombra(d, cx - meio, cy - meio, lado, lado)
    d.ret_c(cx, cy, lado, lado, PRETO_CI, rx=0.12)
    d.ret_c(cx, cy, lado, lado, 'url(#luz)', rx=0.12)
    d.circ(cx - meio + 0.5, cy - meio + 0.5, 0.25, '#3a3c41')
    for k, t in enumerate(linhas):
        d.texto(cx, cy + (k - (len(linhas) - 1) / 2) * 0.8, t, 0.55, MARCA_CI, alinhar='center', peso='normal')


def cristal_smd(d, cx, cy, w=3.2, h=2.5, texto='26.000', vertical=False):
    """O cristal 3225 (a tampa de metal com a gravacao)."""
    if vertical:
        w, h = h, w
    sombra(d, cx - w / 2, cy - h / 2, w, h)
    for sx in (-1, 1):
        for sy in (-1, 1):
            d.ret_c(cx + sx * (w / 2 - 0.45), cy + sy * (h / 2 - 0.4), 0.9, 0.8, ESTANHO)
    d.ret_c(cx, cy, w - 0.3, h - 0.3, 'url(#metal)', rx=0.25, stroke='#7c838b', sw=0.08)
    if texto:
        d.texto(cx, cy, texto, 0.55, '#4f565d', ang=90 if vertical else 0, alinhar='center', peso='normal')


def antena_meandro(d, x, y, w, h, cor=OURO, larg=0.5):
    """A antena de trilha em meandro das ESP (o F invertido em zigue-zague):
    x, y e o canto de cima a esquerda da area da antena."""
    pts = [(x + 0.6, y + h - 0.4), (x + 0.6, y + 0.6)]
    passo = (w - 1.6) / 6
    xx = x + 0.6
    for k in range(6):
        xx += passo
        pts.append((xx, y + 0.6) if k % 2 == 0 else (xx, y + h - 1.6))
        pts.append((xx, y + h - 1.6) if k % 2 == 0 else (xx, y + 0.6))
    d.poli(pts, None, stroke=cor, sw=larg, fechado=False, cap='butt')
    d.linha(x + 0.6, y + h - 0.4, x + 2.4, y + h - 0.4, cor, larg, cap='butt')


def antena_ceramica(d, cx, cy, w=6.0, h=1.8, texto=''):
    sombra(d, cx - w / 2, cy - h / 2, w, h)
    for s in (-1, 1):
        d.ret_c(cx + s * (w / 2 - 0.4), cy, 0.8, h, ESTANHO)
    d.ret_c(cx, cy, w - 1.0, h, 'url(#ceramica)', rx=0.15, stroke='#a9a28f', sw=0.06)
    if texto:
        d.texto(cx, cy, texto, 0.6, '#6a6355', alinhar='center', peso='normal')


def conector_ufl(d, cx, cy):
    """O conector U.FL da antena externa (3 x 3 mm, o anel dourado)."""
    sombra(d, cx - 1.5, cy - 1.5, 3.0, 3.0)
    d.ret_c(cx, cy, 3.0, 3.0, 'url(#metal)', rx=0.2, stroke='#6c737b', sw=0.08)
    d.circ(cx, cy, 1.0, 'url(#ouro)', stroke='#8a6d1f', sw=0.08)
    d.circ(cx, cy, 0.35, '#e8d48a')


def blindagem(d, x, y, w, h, linhas):
    """A tampa de metal dos modulos, com a borda estampada e a gravacao."""
    d.ret(x + 0.15, y + 0.2, w, h, '#000000', rx=0.4, op=0.3)
    d.ret(x, y, w, h, 'url(#blindagem)', rx=0.4, stroke='#7f868d', sw=0.15)
    d.ret(x + 0.5, y + 0.5, w - 1.0, h - 1.0, None, rx=0.3, stroke='#a9b0b6', sw=0.15)
    passo = min(1.25, (h - 2.4) / max(1, len(linhas)))
    y0 = y + h / 2 - (len(linhas) - 1) * passo / 2
    for k, (t, tam) in enumerate(linhas):
        d.texto(x + w / 2, y0 + k * passo, t, tam, '#5b6168', alinhar='center', peso='bold' if k == 0 else 'normal')


def barra_macho(d, xs, y, acabamento='#d6b45a'):
    """A barra macho de 2,54 mm vista de cima: o plastico preto e a ponta
    quadrada de cada pino."""
    x0, x1 = min(xs) - 1.27, max(xs) + 1.27
    sombra(d, x0, y - 1.27, x1 - x0, 2.54)
    d.ret(x0, y - 1.27, x1 - x0, 2.54, 'url(#plastico)', rx=0.2, stroke='#0a0a0b', sw=0.08)
    for x in xs:
        d.ret_c(x, y, 0.9, 0.9, '#6a5218')
        d.ret_c(x, y, 0.64, 0.64, 'url(#ouro)')
