"""python ferramentas/extrair-svg/pecas-drivers-a25.py

Os drivers de motor da Alpha 25, tracados de referencia de verdade (o
TB6612FNG e o DRV8833 saem dos arquivos abertos da SparkFun e da Adafruit,
em placas-arduino.py):

  - a4988, drv8825 : as placas da Pololu (o projeto original dos clones), pelas
                     fotos de cima retificadas (os furos da barra dao a escala)
                     e pelos desenhos com cota (0,6" x 0,8"); o cobre de cima
                     vetorizado da foto (motores-cad/tracado-fotos.json). Na
                     peca ficam deitadas, como no Wokwi: a fileira da potencia
                     (VMOT..GND) em cima, a do controle (EN..DIR) embaixo;
  - l298n          : o modulo vermelho de 43 x 43 mm (o desenho com cota da
                     Handsontec e as fotos de cima): o dissipador em pe, os
                     bornes de 5,08 mm, o 78M05, o jumper do 5V e os do ENA/ENB;
  - l9110s         : o modulo de dois L9110S (29 x 23 mm), bornes MOTOR A/B e
                     a barra de 6 pinos;
  - bts7960        : o IBT-2 (50 x 50 mm, furos a 40 mm): os dois BTS7960, o
                     HC244, o eletrolitico de 330 uF, o borne de 4 vias e a
                     barra 2 x 4;
  - uln2003        : a placa verde do 28BYJ-48 (35 x 32 mm): o ULN2003APG em
                     DIP-16, os quatro LEDs A..D, o conector XH de 5 vias e o
                     jumper da alimentacao;
  - l293d          : o CI de 16 pinos (DIP de 0,3", a pinagem da TI), no mesmo
                     desenho dos 74HC da logica.

Os logotipos (Pololu, Allegro, TI, Handsontec) ficam de fora; a marcacao dos
chips e os rotulos da serigrafia, como nas fotos.
"""
import importlib.util
import io
import json
import math
import os
import sys

AQUI = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, AQUI)
_spec = importlib.util.spec_from_file_location('estilo_a25', os.path.join(AQUI, 'estilo_a25.py'))
E = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(E)

A = E.A
pa = E.pa
mover, rodar, T = E.mover, E.rodar, E.T
OURO, ESTANHO, SERIGRAFIA = E.OURO, E.ESTANHO, E.SERIGRAFIA
PRETO_CI, MARCA_CI = E.PRETO_CI, E.MARCA_CI
PASSO = 2.54

CATALOGO = {p['id']: p for p in json.load(io.open(pa.BASE + 'app/assets/pecas.json', encoding='utf-8'))}
TRACADO = json.load(io.open(os.path.join(AQUI, 'motores-cad', 'tracado-fotos.json'), encoding='utf-8'))
pecas = []
luzes = []          # (peca, x px, y px, r px, cor, chave) para o EditorModulos.cs


DEFS_EXTRA = ('  <radialGradient id="lata-topo" cx="0.4" cy="0.35" r="0.75"><stop offset="0" stop-color="#f1f3f5"/>'
              '<stop offset="0.6" stop-color="#c3c8ce"/><stop offset="1" stop-color="#8a9097"/></radialGradient>\n'
              # o aco zincado amarelado dos parafusos dos bornes (o das fotos)
              '  <radialGradient id="zinco" cx="0.38" cy="0.32" r="0.8"><stop offset="0" stop-color="#eef0d2"/>'
              '<stop offset="0.55" stop-color="#bfc08e"/><stop offset="1" stop-color="#7d7f55"/></radialGradient>\n')


def gravar(nome, d, L, H):
    s = E.svg(d, L, H).replace('</defs>', DEFS_EXTRA + '</defs>', 1)
    io.open(pa.BASE + f'app/assets/{nome}.svg', 'w', encoding='utf-8', newline='').write(s)


def guardar(id_peca, L, H, pinos, descricoes=None):
    """pinos: (nome, x mm, y mm); a descricao e os sinais vem do catalogo
    (ou de descricoes: nome -> (sinais, texto))."""
    antigos = {q['nome']: q for q in CATALOGO.get(id_peca, {}).get('pinos', [])}
    descricoes = descricoes or {}
    saida = []
    for n, x, y in pinos:
        assert 0 < x < L and 0 < y < H, (id_peca, n, x, y)
        s, t = descricoes.get(n, (antigos.get(n, {}).get('sinais', []), antigos.get(n, {}).get('descricao', '')))
        saida.append({'nome': n, 'x': round(x * A, 2), 'y': round(y * A, 2), 'sinais': s, 'descricao': t})
    nomes = [q['nome'] for q in saida]
    assert len(nomes) == len(set(nomes)), nomes
    faltam = set(antigos) - set(nomes)
    assert not faltam, (id_peca, 'sem os pinos', faltam)
    pecas.append({'id': id_peca, 'largura': round(L * A), 'altura': round(H * A), 'pinos': saida,
                  'variantes': [], 'metades': False})
    print(f'{id_peca}: {round(L * A)}x{round(H * A)} px, {len(saida)} pinos')


def luz(peca, x, y, r, cor, chave):
    luzes.append((peca, x * A, y * A, r * A, cor, chave))


# =====================================================================
#  as pecas pequenas, desenhadas no quadro final (mm)
# =====================================================================
def chip_smd(d, cx, cy, w, h, rx=0.05, cor=None, ilha=ESTANHO, ilhas=True):
    """O resistor/capacitor de dois terminais: w x h, os terminais no lado comprido."""
    vertical = h > w
    comp, larg = (h, w) if vertical else (w, h)
    E.sombra(d, cx - w / 2, cy - h / 2, w, h, rx=0.08)
    if ilhas:
        for s in (-1, 1):
            if vertical:
                d.ret_c(cx, cy + s * (comp / 2 - comp * 0.12), larg * 1.15, comp * 0.3, ilha, rx=0.05)
            else:
                d.ret_c(cx + s * (comp / 2 - comp * 0.12), cy, comp * 0.3, larg * 1.15, ilha, rx=0.05)
    d.ret_c(cx, cy, w, h, '#c7cbd0', rx=rx)
    if vertical:
        d.ret_c(cx, cy, w, h * 0.62, cor)
    else:
        d.ret_c(cx, cy, w * 0.62, h, cor)
    d.ret_c(cx, cy, w, h, 'url(#luz)', rx=rx)


TAM = {'0402': (1.0, 0.5), '0603': (1.6, 0.8), '0805': (2.0, 1.25), '1206': (3.2, 1.6), '1210': (3.2, 2.5),
       '2010': (5.0, 2.5)}
COR_R, COR_C = '#1d1d20', '#b58a5a'


def passivo(d, cx, cy, tam, tipo='C', vertical=False, codigo=None, cor=None):
    c, l = TAM[tam]
    w, h = (l, c) if vertical else (c, l)
    chip_smd(d, cx, cy, w, h, cor=cor or (COR_R if tipo == 'R' else COR_C))
    if codigo:
        d.texto(cx, cy, codigo, min(w, h) * 0.45, '#e8e8e8' if (cor or COR_R) != '#f1efe8' else '#2b2b2b',
                ang=90 if vertical else 0, alinhar='center', peso='normal')


def texto_girado(d, x, y, t, tam, cor, graus, peso='normal', ancora='middle'):
    """Texto centrado em (x, y) girado de verdade (o d.texto deixa sempre legivel)."""
    X, Y = d.t(x, y)
    e = d.t.escala
    t = t.replace('&', '&amp;').replace('<', '&lt;')
    d.partes.append(f'<text transform="translate({pa.f(X)} {pa.f(Y)}) rotate({graus})" x="0" y="{pa.f(tam * 0.45 * e)}" '
                    f'font-family="Arial" font-weight="{peso}" font-size="{pa.f(tam * 1.25 * e)}" fill="{cor}" '
                    f'text-anchor="{ancora}">{t}</text>\n')


def corpo_ci(d, cx, cy, w, h, linhas, giro_texto=0, pino1=None, rx=0.15, cor=PRETO_CI):
    E.sombra(d, cx - w / 2, cy - h / 2, w, h, rx=rx)
    d.ret_c(cx, cy, w, h, cor, rx=rx)
    d.ret_c(cx, cy, w, h, 'url(#luz)', rx=rx)
    if pino1:
        d.circ(pino1[0], pino1[1], min(w, h) * 0.07, '#3a3c41')
    n = len(linhas)
    if not n:
        return
    passo = max(t for _, t in linhas) * 1.35
    for k, (t, tam) in enumerate(linhas):
        o = (k - (n - 1) / 2) * passo
        # a linha k anda no eixo perpendicular a escrita
        ang = math.radians(giro_texto)
        dx, dy = -math.sin(ang) * o, math.cos(ang) * o
        texto_girado(d, cx + dx, cy + dy, t, tam, MARCA_CI, giro_texto)


def pernas_lado(d, x0, y0, n, passo, comp, larg, direcao):
    """n pernas de estanho a partir de (x0, y0), andando no eixo do lado;
    direcao: 'cima'/'baixo'/'esq'/'dir' (para onde a perna sai do corpo)."""
    for k in range(n):
        if direcao in ('cima', 'baixo'):
            x = x0 + k * passo
            y = y0 - comp / 2 if direcao == 'cima' else y0 + comp / 2
            d.ret_c(x, y, larg, comp, 'url(#metal-h)', rx=0.03)
        else:
            y = y0 + k * passo
            x = x0 - comp / 2 if direcao == 'esq' else x0 + comp / 2
            d.ret_c(x, y, comp, larg, 'url(#metal)', rx=0.03)


def trimpot(d, cx, cy, base=(3.6, 3.8), r=1.45, giro=0):
    """O trimpot SMD de 3 mm (o do Vref): a base branca, o rotor de metal com a fenda em cruz."""
    w, h = base
    E.sombra(d, cx - w / 2, cy - h / 2, w, h)
    d.ret_c(cx, cy, w, h, '#f2f0ea', rx=0.15, stroke='#b9b5aa', sw=0.06)
    d.circ(cx, cy, r, 'url(#metal)', stroke='#7c838b', sw=0.08)
    d.circ(cx, cy, r * 0.55, '#c9ced4', stroke='#8d949b', sw=0.06)
    d.entrar(mover(cx, cy) * rodar(giro))
    d.ret_c(0, 0, r * 1.5, r * 0.32, '#5b6168', rx=0.05)
    d.ret_c(0, 0, r * 0.32, r * 1.5, '#5b6168', rx=0.05)
    d.sair()
    d.circ(cx, cy, r * 0.18, '#2a2d31')


def ilha_redonda(d, x, y, anel=1.7, furo=1.0, quadrada=False, acabamento=ESTANHO):
    if quadrada:
        d.ret_c(x, y, anel, anel, acabamento, rx=0.12)
    else:
        d.circ(x, y, anel / 2, acabamento)
    d.circ(x, y, anel / 2 * 0.93, None, '#ffffff', 0.08, op=0.35)
    d.circ(x, y, furo / 2, E.FURO)


def cobre_da_foto(d, cor, mod, P):
    """O cobre de cima vetorizado da foto (mm da foto), passado pelo ajuste P."""
    t = TRACADO[mod]
    for poli in t['cobre']:
        cmds = []
        for anel in poli:
            pts = [P(x, y) for x, y in anel]
            cmds += [('M', *pts[0])] + [('L', *p) for p in pts[1:]] + [('Z',)]
        d.caminho(cmds, cor)
        d.partes[-1] = d.partes[-1].replace('<path ', '<path fill-rule="evenodd" ', 1)


def via(d, x, y, cor_cobre, cor_furo, r=0.32):
    d.circ(x, y, r, cor_cobre)
    d.circ(x, y, r * 0.48, cor_furo)


# =====================================================================
#  A4988 e DRV8825 (Pololu), deitados
# =====================================================================
def ajuste_pololu(x0, y0, x1, y1):
    """O mm da foto retificada -> o mm da placa deitada. (x0, y0) e (x1, y1):
    os centros dos furos do canto de cima (pino 1 da esquerda, da foto) e de
    baixo da direita; na placa eles ficam em (1,27; 1,27) e (13,97; 19,05).
    Deitada: a coluna da direita da foto (VMOT..GND) vira a fileira de cima."""
    sx, sy = 12.70 / (x1 - x0), 17.78 / (y1 - y0)

    def P(u, v):
        un, vn = 1.27 + (u - x0) * sx, 1.27 + (v - y0) * sy
        return vn, 15.24 - un
    return P


def driver_pololu(id_peca, cor, mod, x0, y0, x1, y1, desenhar_pecas, cima, baixo, acabamento):
    L, H = E.caixa(20.32, 15.24)
    d = E.novo()
    mascara, borda, cobre = cor
    d.ret(0.12, 0.16, 20.32, 15.24, '#000000', rx=0.2, op=0.25)
    d.ret(0, 0, 20.32, 15.24, mascara, rx=0.2, stroke=borda, sw=0.15)
    P = ajuste_pololu(x0, y0, x1, y1)
    cobre_da_foto(d, cobre, mod, P)
    for x, y in TRACADO[mod]['vias']:
        via(d, *P(x, y), cobre, borda)
    d.ret(0, 0, 20.32, 15.24, 'url(#luz)', rx=0.2)
    # as ilhas da barra: a de cima e a potencia (os GND quadrados, como na placa)
    pinos = []
    for k, n in enumerate(cima):
        x = 1.27 + k * PASSO
        ilha_redonda(d, x, 1.27, 1.75, 1.0, quadrada=n.startswith('GND'), acabamento=acabamento)
        pinos.append((n, x, 1.27))
    for k, n in enumerate(baixo):
        x = 1.27 + k * PASSO
        ilha_redonda(d, x, 13.97, 1.75, 1.0, acabamento=acabamento)
        pinos.append((n, x, 13.97))
    desenhar_pecas(d, P)
    gravar(id_peca, d, L, H)
    guardar(id_peca, L, H, pinos)


def pecas_a4988(d, P):
    """As pecas da foto de cima (mm da foto, retrato): as posicoes medidas na
    regua de 0,5 mm da foto retificada."""
    def R(u, v, tam, tipo, vertical_foto, codigo=None, cor=None):
        x, y = P(u, v)
        passivo(d, x, y, tam, tipo, vertical=not vertical_foto, codigo=codigo, cor=cor)
    R(3.75, 1.18, '0603', 'R', False, '01D')
    R(3.2, 3.55, '0603', 'C', True)
    R(6.6, 1.8, '0603', 'C', True)
    R(5.65, 4.05, '0805', 'C', False)
    R(9.65, 1.75, '1206', 'C', False)
    R(9.4, 4.35, '0603', 'C', True)
    R(11.25, 4.85, '1206', 'R', True, 'R068', cor='#f1efe8')
    R(9.4, 14.2, '0603', 'C', True)
    R(11.25, 13.9, '1206', 'R', True, 'R068', cor='#f1efe8')
    R(5.6, 14.75, '0805', 'C', False)
    R(3.2, 15.0, '0603', 'R', True, '01D')
    R(3.2, 18.4, '0603', 'R', True, '01C')
    R(12.0, 17.6, '0603', 'R', True, '3E')
    for v in (17.1, 18.95):
        x, y = P(5.0, v)
        d.ret_c(x, y, 0.85, 0.85, ESTANHO, rx=0.08)
    # o A4988 (QFN-28 de 5 x 5 mm, 7 pernas de 0,5 mm por lado)
    cx, cy = P(5.62, 9.45)
    for k in range(7):
        o = (k - 3) * 0.5
        for s in (-1, 1):
            d.ret_c(cx + o, cy + s * 2.62, 0.24, 0.5, ESTANHO, rx=0.04)
            d.ret_c(cx + s * 2.62, cy + o, 0.5, 0.24, ESTANHO, rx=0.04)
    corpo_ci(d, cx, cy, 5.0, 5.0, [('4988ET', 0.85), ('1826', 0.8), ('443H', 0.8)], pino1=(cx + 1.8, cy + 1.8))
    trimpot(d, *P(8.8, 18.05), base=(3.8, 4.2), r=1.5)


def pecas_drv8825(d, P):
    def R(u, v, tam, tipo, vertical_foto, codigo=None, cor=None):
        x, y = P(u, v)
        passivo(d, x, y, tam, tipo, vertical=not vertical_foto, codigo=codigo, cor=cor)
    trimpot(d, *P(4.15, 2.55), base=(3.4, 3.9), r=1.45)
    for u in (7.15, 9.25):
        x, y = P(u, 2.9)
        chip_smd(d, x, y, 3.9, 1.8, cor='#1d1d20')
        d.texto(x, y, 'R100', 0.8, '#e8e8e8', alinhar='center', peso='normal')
    R(11.5, 2.75, '1210', 'C', True)
    R(4.05, 6.1, '0805', 'C', False)
    R(4.05, 7.65, '0805', 'C', False)
    R(10.35, 7.15, '0603', 'C', True)
    R(11.85, 7.2, '0603', 'C', True)
    R(3.95, 19.2, '0805', 'C', False)
    R(7.3, 19.2, '0805', 'R', False, '01C')
    R(10.65, 19.2, '0805', 'R', False, '18b')
    # o DRV8825 (HTSSOP-28, 9,7 x 4,4 mm, 14 pernas de 0,65 mm por lado), de
    # ponta-cabeca na foto: o pino 1 em cima a direita
    cx, cy = P(7.95, 13.3)
    for k in range(14):
        o = (k - 6.5) * 0.65
        for s in (-1, 1):
            d.ret_c(cx + s * 3.0, cy + o, 1.4, 0.32, 'url(#metal)', rx=0.04)
    corpo_ci(d, cx, cy, 4.4, 9.7, [('DRV8825', 0.95), ('29TC', 0.85), ('AJLV', 0.85)], giro_texto=-90,
             pino1=(cx + 1.4, cy - 3.9))
    via(d, *P(13.05, 9.95), OURO, '#a8892f', r=0.55)


driver_pololu('a4988', ('#0a6042', '#06402b', '#239257'), 'a4988', 0.93, 1.04, 14.16, 19.19, pecas_a4988,
              ['VMOT', 'GND', '2B', '2A', '1A', '1B', 'VDD', 'GND.2'],
              ['EN', 'MS1', 'MS2', 'MS3', 'RST', 'SLP', 'STEP', 'DIR'], ESTANHO)
driver_pololu('drv8825', ('#33205a', '#1f1238', '#563782'), 'drv8825', 0.97, 0.94, 14.14, 19.06, pecas_drv8825,
              ['VMOT', 'GND', 'B2', 'B1', 'A1', 'A2', 'FLT', 'GND.2'],
              ['EN', 'M0', 'M1', 'M2', 'RST', 'SLP', 'STEP', 'DIR'], '#e3cf8f')


# =====================================================================
#  pecas comuns das placas de borne
# =====================================================================
AZUL_KF, AZUL_KF_ESCURO, AZUL_KF_BORDA = '#3a97d0', '#2a78b8', '#17507e'


def parafuso_kf(d, x, y, giro=45):
    """O parafuso do borne visto de cima: o poco escuro, a cabeca de aco com a
    fenda em cruz (o Phillips/fenda dos KF301)."""
    d.circ(x, y, 1.78, '#0f2c48')
    d.circ(x, y, 1.45, 'url(#zinco)', stroke='#5f6046', sw=0.1)
    d.circ(x, y, 1.45, None, '#ffffff', 0.06, op=None)
    d.entrar(mover(x, y) * rodar(giro))
    d.ret_c(0, 0, 2.3, 0.42, '#4f513a', rx=0.08)
    d.ret_c(0, 0, 0.42, 2.3, '#4f513a', rx=0.08)
    d.sair()
    d.circ(x - 0.45, y - 0.5, 0.35, '#ffffff', op=0.35)


def borne_kf301(d, x0, y0, x1, y1, parafusos, furos, divisorias, faixa):
    """O borne KF301 (5,08 mm) visto de cima, no quadro dado: o corpo azul, a
    faixa mais escura dos furinhos de teste (faixa: x0, y0, x1, y1), as
    divisorias entre as vias e os parafusos."""
    E.sombra(d, x0, y0, x1 - x0, y1 - y0, rx=0.4, op=0.35)
    d.ret(x0, y0, x1 - x0, y1 - y0, AZUL_KF, rx=0.35, stroke=AZUL_KF_BORDA, sw=0.12)
    fx0, fy0, fx1, fy1 = faixa
    d.ret(fx0, fy0, fx1 - fx0, fy1 - fy0, AZUL_KF_ESCURO, rx=0.25)
    d.ret(x0, y0, x1 - x0, y1 - y0, 'url(#luz)', rx=0.35)
    for x, y in furos:
        d.circ(x, y, 0.62, '#0f2c48')
        d.circ(x + 0.1, y + 0.12, 0.42, '#0a1d30')
    for xa, ya, xb, yb in divisorias:
        d.linha(xa, ya, xb, yb, '#0f2c48', 0.55)
    for x, y in parafusos:
        parafuso_kf(d, x, y)


def pino_jumper(d, x, y):
    d.ret_c(x, y, 2.5, 2.5, 'url(#plastico)', rx=0.15, stroke='#0a0a0b', sw=0.06)
    d.ret_c(x, y, 0.84, 0.84, '#7a5c18')
    d.ret_c(x, y, 0.64, 0.64, 'url(#ouro)')


def capacete(d, cx, cy, w, h, horizontal):
    """O jumper (o capacete preto) visto de cima, com a lingueta de metal da ponte."""
    E.sombra(d, cx - w / 2, cy - h / 2, w, h, rx=0.3, op=0.35)
    d.ret_c(cx, cy, w, h, '#1d1e22', rx=0.3, stroke='#0a0a0b', sw=0.08)
    d.ret_c(cx, cy, w, h, 'url(#luz)', rx=0.3)
    if horizontal:
        d.ret_c(cx, cy - h * 0.12, w * 0.62, h * 0.16, 'url(#metal-h)', rx=0.05)
        d.ret_c(cx, cy + h * 0.2, w * 0.22, h * 0.22, '#0b0b0d', rx=0.05)
    else:
        d.ret_c(cx - w * 0.12, cy, w * 0.16, h * 0.62, 'url(#metal)', rx=0.05)
        d.ret_c(cx + w * 0.2, cy, w * 0.22, h * 0.22, '#0b0b0d', rx=0.05)


def eletrolitico_vchip(d, cx, cy, base, r, menos, linhas, giro_texto, dx=0.0, dy=0.0):
    """O eletrolitico SMD visto de cima: a base de plastico preta (os cantos do
    + chanfrados), os terminais, a lata (o topo prateado com a faixa cinza do -)
    e o valor impresso. menos: o lado da faixa ('esq'/'dir'/'cima'/'baixo')."""
    b = base / 2
    k = 1.2
    d.ret(cx - b + 0.15, cy - b + 0.2, base, base, '#000000', rx=0.4, op=0.3)
    # os terminais saem pelos lados da faixa e do +
    if menos in ('esq', 'dir'):
        for s in (-1, 1):
            d.ret_c(cx + s * (b + 0.12), cy, 0.6, 2.4, ESTANHO, rx=0.08)
    else:
        for s in (-1, 1):
            d.ret_c(cx, cy + s * (b + 0.12), 2.4, 0.6, ESTANHO, rx=0.08)
    sx = {'esq': 1, 'dir': -1}.get(menos, 0)
    sy = {'cima': 1, 'baixo': -1}.get(menos, 0)
    pts = []
    for px, py in ((-b, -b), (b, -b), (b, b), (-b, b)):
        # chanfra os dois cantos do lado do + (o oposto da faixa)
        if (sx and px * sx > 0) or (sy and py * sy > 0):
            if sx:
                pts += [(px - sx * k, py), (px, py + (k if py < 0 else -k))] if py < 0 else [(px, py - k), (px - sx * k, py)]
            else:
                pts += [(px, py - sy * k), (px + (k if px < 0 else -k), py)] if px < 0 else [(px - k, py), (px, py - sy * k)]
        else:
            pts.append((px, py))
    d.poli([(cx + px, cy + py) for px, py in pts], '#232428', stroke='#0e0f11', sw=0.1)
    lx, ly = cx + dx, cy + dy
    d.circ(lx + 0.12, ly + 0.15, r, '#000000', op=0.3)
    d.circ(lx, ly, r, 'url(#lata-topo)', stroke='#7a8088', sw=0.14)
    # a faixa do - (um quarto do topo, cinza escuro)
    h = r * 0.5
    w = math.sqrt(r * r - h * h)
    ang = {'esq': 90, 'dir': -90, 'cima': 180, 'baixo': 0}[menos]
    d.entrar(mover(lx, ly) * rodar(ang))
    d.caminho([('M', -w, h), ('A', r, 0, 0, w, h), ('Z',)], '#7d838b', op=0.9)
    d.sair()
    d.circ(lx, ly, r * 0.93, None, '#ffffff', 0.08, op=None)
    for k2, (t, tam, ox, oy) in enumerate(linhas):
        texto_girado(d, lx + ox, ly + oy, t, tam, '#3a3d42', giro_texto, peso='bold')


def furo_fixacao(d, x, y, anel=5.3, furo=3.0, furinhos=8):
    """O furo de fixacao de anel estanhado com os furinhos de costura em volta."""
    d.circ(x, y, anel / 2, ESTANHO)
    d.circ(x, y, anel / 2, 'url(#luz)')
    d.circ(x, y, anel / 2 * 0.96, None, '#ffffff', 0.1, op=None)
    for k in range(furinhos):
        a = math.radians(22.5 + k * 360 / furinhos)
        d.circ(x + 2.15 * math.cos(a), y + 2.15 * math.sin(a), 0.17, '#5a5f66')
    d.circ(x, y, furo / 2, E.FURO)


# =====================================================================
#  L298N (o modulo vermelho de 43 x 43 mm)
# =====================================================================
def l298n():
    """Medido na foto de cima da Handsontec (retificada em 40 px/mm pelos
    cantos da placa; as linhas de anotacao tiradas por inpaint). A foto tem o
    dissipador embaixo: o desenho e feito no quadro da foto e girado 180 graus
    (na peca, como no Wokwi e na LastMinuteEngineers, o dissipador fica em
    cima, o OUT1/OUT2 a esquerda, o borne 12V/GND/5V embaixo a esquerda e a
    barra ENA..ENB embaixo a direita)."""
    L, H = E.caixa(43.0, 43.0)
    d = E.novo()
    mascara, borda, cobre = '#a3222a', '#5e1216', '#de3b2e'
    d.ret(0.15, 0.2, 43, 43, '#000000', rx=0.6, op=0.3)
    d.ret(0, 0, 43, 43, mascara, rx=0.6, stroke=borda, sw=0.18)
    d.entrar(T(-1, 0, 0, -1, 43, 43))
    cobre_da_foto(d, cobre, 'l298n', lambda u, v: (u, v))
    vias = [(x, y) for y in (19.2, 24.1, 27.8, 32.05) for x in (1.65, 2.28, 2.9)] + [(2.28, 28.4), (2.9, 28.4)]
    vias += [(x + 38.5, y) for x, y in vias if y in (24.1, 32.05)] + [(40.2, 28.4), (40.78, 28.4)]
    vias += [(19.83, 12.85), (20.4, 12.85), (19.83, 13.4), (20.4, 13.4)]
    vias += [(x, y) for x in (32.1, 32.6) for y in (14.2, 14.7, 15.2)]
    vias += [(x, y) for x in (8.2, 8.7, 9.2) for y in (30.0, 30.5)]
    for x, y in vias:
        via(d, x, y, cobre, borda, r=0.2)
    d.ret(0, 0, 43, 43, 'url(#luz)', rx=0.6)

    # ---- a serigrafia ----
    S, sw = SERIGRAFIA, 0.22
    def sl(x1, y1, x2, y2):
        d.linha(x1, y1, x2, y2, S, sw, cap='square')

    def sr(x0, y0, x1, y1):
        d.ret(x0, y0, x1 - x0, y1 - y0, None, stroke=S, sw=sw)
    # os diodos: duas colunas de quatro caixas
    celulas = [(19.8, 23.75), (24.0, 28.1), (28.35, 32.55), (32.8, 37.0)]
    for dx in (0.0, 34.1):
        for ya, yb in celulas:
            sr(0.7 + dx, ya, 7.95 + dx, yb)
            sl(5.0 + dx, ya, 5.0 + dx, ya + 0.75)
            sl(5.0 + dx, yb - 0.75, 5.0 + dx, yb)
    # o regulador, o resistor, o LED e o jumper do 5V
    sl(10.15, 13.9, 20.95, 13.9)
    sl(20.95, 13.9, 20.95, 14.55)
    sl(10.15, 13.9, 10.15, 21.65)
    sl(10.15, 21.65, 20.95, 21.65)
    sl(20.95, 21.0, 20.95, 21.65)
    sr(22.65, 10.15, 27.4, 12.65)
    sr(20.15, 8.65, 21.85, 11.95)
    sr(27.8, 9.3, 33.65, 12.9)
    # o L298N: a linha das pernas com os risquinhos e as laterais
    sl(11.3, 27.9, 31.5, 27.9)
    for k in range(7):
        x = 21.415 + (k - 3) * 2.54
        sl(x, 27.9, x, 27.35)
    sl(11.3, 27.9, 11.3, 29.6)
    sl(31.5, 27.9, 31.5, 29.6)
    # os retangulos do dissipador (aparecem entre as aletas)
    for x0, y0, x1 in ((10.65, 38.3, 14.15), (15.15, 35.9, 18.65), (19.4, 38.3, 23.15), (25.15, 35.9, 28.9),
                       (29.65, 38.3, 33.3)):
        sr(x0, y0, x1, 42.9)
    # os rotulos da barra (de cabeca para baixo na foto: na peca, lidos direito)
    for k, t in enumerate(['ENB', 'IN4', 'IN3', 'IN2', 'IN1', 'ENA']):
        d.texto(7.2 + k * 2.54, 0.75, t, 0.85, S, alinhar='center', peso='bold')

    # ---- os furos de fixacao (37,2 mm entre centros) ----
    for x in (2.92, 40.08):
        for y in (2.92, 40.08):
            furo_fixacao(d, x, y)

    # ---- os diodos M7 (SMA), as ilhas e os terminais de estanho ----
    for dx in (0.0, 34.1):
        for (ya, yb), yc in zip(celulas, (21.8, 26.0, 30.4, 34.65)):
            for x0, x1 in ((0.95, 2.55), (5.75, 7.7)):
                d.ret(x0 + dx, ya + 0.35, x1 - x0, yb - ya - 0.7, ESTANHO, rx=0.12)
            cx = 4.12 + dx
            for s in (-1, 1):
                d.ret_c(cx + s * 2.3, yc, 1.1, 1.5, 'url(#metal)', rx=0.15, stroke='#7a8088', sw=0.06)
            E.sombra(d, cx - 1.98, yc - 1.2, 3.96, 2.4, rx=0.2)
            d.ret_c(cx, yc, 3.96, 2.4, '#1f2023', rx=0.2)
            d.ret_c(cx, yc, 3.96, 2.4, 'url(#luz)', rx=0.2)
            d.texto(cx, yc, 'M7', 0.95, '#5c6066', alinhar='center', peso='bold')

    # ---- o regulador 78M05 (DPAK): a aba a esquerda, as pernas a direita ----
    for y0, y1 in ((14.5, 16.15), (19.15, 20.8)):
        d.ret(19.4, y0, 1.6, y1 - y0, ESTANHO, rx=0.1)
    d.ret(17.75, 17.15, 0.65, 1.0, ESTANHO, rx=0.08)
    for yc in (15.35, 19.95):
        d.ret(18.0, yc - 0.4, 2.7, 0.8, 'url(#metal-h)', rx=0.08)
    d.ret(18.0, 17.35, 0.7, 0.75, 'url(#metal-h)', rx=0.05)
    d.ret(10.8, 15.1, 1.3, 5.5, 'url(#metal)', rx=0.12, stroke='#7a8088', sw=0.06)
    E.sombra(d, 11.9, 14.5, 6.1, 6.4, rx=0.25)
    d.ret(11.9, 14.5, 6.1, 6.4, '#1d1e21', rx=0.25)
    d.ret(11.9, 14.5, 6.1, 6.4, 'url(#luz)', rx=0.25)
    d.circ(13.15, 17.8, 0.35, None, '#3a3c41', 0.12)
    texto_girado(d, 15.6, 17.9, '78M05', 0.85, MARCA_CI, 180)

    # ---- os dois eletroliticos de 220 uF / 35 V (a faixa do - a esquerda na foto) ----
    linhas = [('220', 1.55, 0.65, -1.85), ('35V', 1.55, 0.65, 0.0), ('VS', 1.55, 0.65, 1.85)]
    eletrolitico_vchip(d, 14.97, 9.35, 8.5, 4.05, 'esq', [(t, s, -ox, -oy) for t, s, ox, oy in linhas], 180)
    eletrolitico_vchip(d, 28.2, 17.95, 8.4, 3.95, 'esq', [(t, s, -ox, -oy) for t, s, ox, oy in linhas], 180)

    # ---- o LED da energia, o resistor dele e o jumper do 5V-EN ----
    d.ret_c(21.05, 10.35, 1.25, 2.0, ESTANHO, rx=0.08)
    d.ret_c(21.05, 10.35, 1.25, 1.3, '#efece2')
    d.ret_c(21.05, 10.35, 0.75, 0.75, '#e6d8c4', rx=0.12)
    d.ret_c(21.05, 10.35, 0.3, 0.3, '#b49c74')
    chip_smd(d, 25.1, 11.35, 3.2, 1.6, cor='#1d1d20')
    texto_girado(d, 25.1, 11.35, '1001', 0.75, '#e8e8e8', 180)
    for x in (29.43, 31.97):
        pino_jumper(d, x, 11.1)
    capacete(d, 30.7, 11.1, 5.0, 2.5, True)

    # ---- a barra: IN1..IN4 e os dois jumpers verticais do ENA/ENB ----
    xs = [7.2 + k * 2.54 for k in range(6)]
    E.sombra(d, xs[1] - 1.27, 0.83, 4 * 2.54, 2.54, rx=0.15)
    for x in xs[1:5]:
        pino_jumper(d, x, 2.1)
    for x in (xs[0], xs[5]):
        for y in (2.1, 4.64):
            pino_jumper(d, x, y)
        capacete(d, x, 3.37, 2.5, 5.0, False)

    # ---- os bornes: 12V/GND/5V (o de 3 vias) e as saidas ----
    borne_kf301(d, 22.0, 0.0, 37.4, 7.4, [(29.43 + k * 5.08, 3.65) for k in (-1, 0, 1)],
                [(29.43 + k * 5.08, 6.4) for k in (-1, 0, 1)], [(26.89, 1.7, 26.89, 5.1), (31.97, 1.7, 31.97, 5.1)],
                (22.0, 5.6, 37.4, 7.4))
    borne_kf301(d, 0.15, 7.95, 8.15, 18.45, [(4.0, 13.0 + s * 2.54) for s in (-1, 1)],
                [(6.75, 13.0 + s * 2.54) for s in (-1, 1)], [(2.2, 13.0, 5.8, 13.0)], (5.6, 7.95, 8.15, 18.45))
    borne_kf301(d, 34.85, 7.95, 42.6, 18.45, [(39.15, 13.0 + s * 2.54) for s in (-1, 1)],
                [(36.0, 13.0 + s * 2.54) for s in (-1, 1)], [(37.3, 13.0, 40.9, 13.0)], (34.85, 7.95, 37.3, 18.45))

    # ---- o L298N (Multiwatt15 em pe) e o dissipador preto de aletas ----
    PRETO_D, BORDA_D = '#18191c', '#3d4046'
    for k in range(7):                                  # as pernas curtas: so a solda aparece
        x = 21.415 + (k - 3) * 2.54
        d.circ(x, 29.45, 0.78, ESTANHO)
        d.circ(x, 29.45, 0.78, 'url(#luz)')
    for k in range(8):                                  # as pernas compridas, dobradas ate a fileira de tras
        x = 21.415 + (k - 3.5) * 2.54
        d.circ(x, 24.4, 0.88, ESTANHO)
        d.circ(x, 24.4, 0.88, 'url(#luz)')
        d.ret(x - 0.33, 24.35, 0.66, 5.5, 'url(#metal-h)', rx=0.12)
        d.circ(x, 24.4, 0.4, '#e9ecef', op=0.7)
    # o dissipador: as laterais, o corpo atras do CI, a base e as seis aletas
    for x0, x1 in ((9.6, 10.35), (33.55, 34.3)):
        d.ret(x0, 27.2, x1 - x0, 43 - 27.2, PRETO_D, stroke=BORDA_D, sw=0.08)
    for x0, x1 in ((10.35, 11.15), (31.8, 33.55)):
        d.ret(x0, 28.0, x1 - x0, 7.1, '#202125')
    d.ret(9.6, 35.05, 34.3 - 9.6, 1.85, PRETO_D, stroke=BORDA_D, sw=0.08)
    for xc in (14.59, 19.03, 24.94, 29.19):
        d.ret(xc - 0.22, 36.9, 0.44, 43 - 36.9, PRETO_D, stroke=BORDA_D, sw=0.06)
    for x0 in (9.6, 33.55):
        d.linha(x0 + 0.08, 27.25, x0 + 0.08, 43, '#5d6168', 0.12, cap='butt')
    for x, y in ((12.27, 36.5), (31.6, 36.5)):          # os pinos de solda do dissipador
        d.circ(x, y, 0.62, 'url(#metal)', stroke='#6d737b', sw=0.06)
    # o parafuso que prende o CI: a ponta com a rosca entre as aletas do meio
    d.ret(20.25, 37.0, 2.6, 1.0, 'url(#metal-h)', rx=0.15)
    for k in range(6):
        d.linha(20.45 + k * 0.42, 37.05, 20.25 + k * 0.42, 37.95, '#4a4f56', 0.1)
    # o CI: o topo do corpo, a aba de cobre estanhada e a cabeca do parafuso
    E.sombra(d, 11.15, 29.75, 20.65, 5.05, rx=0.2)
    d.ret(11.15, 29.75, 20.65, 5.05, '#202226', rx=0.2, stroke='#0c0d0f', sw=0.08)
    d.ret(11.15, 29.75, 20.65, 5.05, 'url(#luz)', rx=0.2)
    d.ret(13.4, 33.35, 16.1, 1.5, 'url(#metal-h)', rx=0.1, stroke='#7a8088', sw=0.06)
    d.caminho([('M', 18.9, 32.65), ('L', 19.4, 31.85), ('L', 23.6, 31.85), ('L', 24.15, 32.65), ('Z',)],
              'url(#metal-h)', '#6d737b', 0.06)
    d.sair()

    # ---- os pinos (no quadro da peca: a foto girada 180 graus) ----
    def Q(u, v):
        return round(43 - u, 3), round(43 - v, 3)
    pinos = []
    for n, k in (('OUT1', -1), ('OUT2', 1)):
        pinos.append((n, *Q(39.15, 13.0 - k * 2.54)))
    for n, k in (('OUT4', -1), ('OUT3', 1)):
        pinos.append((n, *Q(4.0, 13.0 - k * 2.54)))
    for n, k in (('12V', 1), ('GND', 0), ('5V', -1)):
        pinos.append((n, *Q(29.43 + k * 5.08, 3.65)))
    for n, x in zip(['ENB', 'IN4', 'IN3', 'IN2', 'IN1', 'ENA'], xs):
        pinos.append((n, *Q(x, 2.1)))
    pinos.append(('5V.2', *Q(xs[5], 4.64)))
    pinos.append(('5V.3', *Q(xs[0], 4.64)))
    gravar('l298n', d, L, H)
    guardar('l298n', L, H, pinos, {
        '5V.2': (['power'], 'O +5V do jumper do ENA (o mesmo 5V): com o jumper no lugar, o canal A fica sempre ligado'),
        '5V.3': (['power'], 'O +5V do jumper do ENB (o mesmo 5V): com o jumper no lugar, o canal B fica sempre ligado')})
    luz('l298n', *Q(21.05, 10.35), 0.9, '#ff3b30', 'energia')


l298n()


# =====================================================================
#  BTS7960 (o IBT-2: 49,657 x 49,403 mm)
# =====================================================================
VERDE_KF, VERDE_KF_ESCURO, VERDE_KF_BORDA = '#3dae4c', '#2c8a3a', '#1b5a25'


def borne_kf301_verde(d, x0, x1, y0, y1, xs, y_parafuso):
    """O KF301 verde de 2 vias (5,08 mm): a boca dos fios virada para a borda (y0)."""
    E.sombra(d, x0, y0, x1 - x0, y1 - y0, rx=0.4, op=0.35)
    d.ret(x0, y0, x1 - x0, y1 - y0, VERDE_KF, rx=0.35, stroke=VERDE_KF_BORDA, sw=0.12)
    d.ret(x0 + 0.1, y0, x1 - x0 - 0.2, 1.1, VERDE_KF_ESCURO, rx=0.25)          # o degrau da boca dos fios
    d.ret(x0, y0, x1 - x0, y1 - y0, 'url(#luz)', rx=0.35)
    d.linha((x0 + x1) / 2, y0 + 1.5, (x0 + x1) / 2, y1 - 0.9, '#1b5a25', 0.5)  # a divisoria
    for x in xs:
        d.circ(x, y1 - 1.25, 0.55, '#16441d')
        parafuso_kf(d, x, y_parafuso)


def bts7960():
    """Pelo desenho da placa com cota (o da serigrafia, retificado em 20 px/mm:
    as ilhas dos dois BTS7960 em TO-263-7, do 74HC244 em SOIC-20W, dos oito
    resistores, do C2; os furos a 40 mm; o circulo do C1; o borne P2 e a barra
    P3) e pelas fotos do modulo (o eletrolitico de 330 uF / 50 V em pe, os
    bornes verdes, os parafusos de fixacao do dissipador, a marcacao dos CIs).
    Os logotipos e os nomes do vendedor (Arduino, NISI, wingxin, ALFS-TECH) e
    os selos (UL, CE, a lixeira) ficam de fora; o IBT-2 fica."""
    W, Hh = 49.657, 49.403
    L, H = E.caixa(W, Hh)
    d = E.novo()
    mascara, borda = '#1f5fae', '#123b72'
    c1, c2 = 4.8, 2.2
    contorno = [(c1, 0), (W - c1, 0), (W, c1), (W, Hh - c2), (W - c2, Hh), (c2, Hh), (0, Hh - c2), (0, c1)]
    d.poli([(x + 0.15, y + 0.2) for x, y in contorno], '#000000', op=0.3)
    d.poli(contorno, mascara, stroke=borda, sw=0.18)
    d.poli(contorno, 'url(#luz)')
    furos = [(4.83, 6.3), (44.5, 6.3), (4.83, 46.55), (44.5, 46.55)]

    # ---- a serigrafia ----
    S, sw = SERIGRAFIA, 0.2
    def sl(x1, y1, x2, y2):
        d.linha(x1, y1, x2, y2, S, sw, cap='square')
    # os aneis dos furos dos cantos (dois quadrantes cheios, como na placa)
    for x, y in furos:
        d.circ(x, y, 2.6, None, S, 0.6)
        for a0 in (180, 0):
            a, b = math.radians(a0), math.radians(a0 + 90)
            d.caminho([('M', x + 2.3 * math.cos(a), y + 2.3 * math.sin(a)),
                       ('A', 2.3, 0, 1, x + 2.3 * math.cos(b), y + 2.3 * math.sin(b))], None, S, 1.2, cap='butt')
    # o C1: o circulo com a metade do - hachurada
    d.circ(14.32, 5.32, 4.81, None, S, sw)
    d.texto(9.0, 1.4, 'C1', 0.85, S, alinhar='center', peso='normal')
    d.texto(41.6, 1.4, 'P2', 0.85, S, alinhar='center', peso='normal')
    d.texto(44.5, 13.4, 'IBT-2', 2.1, S, alinhar='center', peso='normal')
    # os BTS7960 (U2, U3) e o 74HC244 (U1), o C2 e os resistores
    d.texto(13.1, 21.6, 'U2', 0.85, S, alinhar='center', peso='normal')
    d.texto(37.4, 21.6, 'U3', 0.85, S, alinhar='center', peso='normal')
    d.texto(48.3, 30.2, 'U1', 0.85, S, ang=90, alinhar='center', peso='normal')
    d.texto(44.0, 26.6, 'C2', 0.85, S, ang=90, alinhar='center', peso='normal')
    d.ret(43.2, 27.6, 4.1, 2.1, None, stroke=S, sw=sw)
    for x0, x1 in ((14.45, 24.3), (25.25, 35.1)):
        d.ret(x0, 30.35, x1 - x0, 1.55, None, stroke=S, sw=sw)
    for k, (a, b) in enumerate(zip(['R4', 'R5', 'R9', 'R3'], ['R7', 'R6', 'R8', 'R2'])):
        d.texto(15.2 + k * 0.92, 40.2, a, 0.72, S, ang=90, alinhar='center', peso='normal')
        d.texto(15.2 + k * 0.92, 42.45, b, 0.72, S, ang=90, alinhar='center', peso='normal')
    # a tabela da barra P3
    for y in (44.15, 46.7, 49.2):
        sl(9.4, y, 39.7, y)
    for x in (9.4, 12.8, 16.65, 20.15, 23.65, 29.55, 39.7):
        sl(x, 44.15, x, 49.2)
    for k, (a, b) in enumerate(zip(['VCC', 'R_IS', 'R_EN', 'RPWM'], ['GND', 'L_IS', 'L_EN', 'LPWM'])):
        xc = (9.4, 12.8, 16.65, 20.15, 23.65)[k:k + 2]
        d.texto(sum(xc) / 2, 45.42, a, 0.7, S, alinhar='center', peso='normal')
        d.texto(sum(xc) / 2, 47.95, b, 0.7, S, alinhar='center', peso='normal')
    d.texto(28.15, 45.42, '7', 1.5, S, alinhar='center', peso='normal')
    d.texto(28.15, 47.95, '8', 1.5, S, alinhar='center', peso='normal')
    d.texto(40.6, 45.42, '1', 1.5, S, alinhar='center', peso='normal')
    d.texto(40.6, 47.95, '2', 1.5, S, alinhar='center', peso='normal')
    d.texto(40.6, 43.3, 'P3', 0.85, S, alinhar='center', peso='normal')

    # ---- os furos: os dos cantos e os dois do meio (os parafusos do dissipador) ----
    for x, y in furos:
        d.circ(x, y, 1.6, None, ESTANHO, 0.5)
        d.circ(x, y, 1.45, E.FURO)
    for x, y in ((11.08, 27.5), (38.33, 27.45)):
        d.circ(x, y, 2.55, None, S, 0.35)
        d.circ(x + 0.12, y + 0.18, 2.7, '#000000', op=0.3)
        d.circ(x, y, 2.7, 'url(#metal)', stroke='#6d737b', sw=0.1)
        d.circ(x, y, 2.1, None, '#ffffff', 0.12, op=None)
        d.entrar(mover(x, y) * rodar(20))
        d.ret_c(0, 0, 3.0, 0.5, '#5b6168', rx=0.1)
        d.ret_c(0, 0, 0.5, 3.0, '#5b6168', rx=0.1)
        d.sair()
    for x in (14.25, 24.75):
        for y in (34.6, 36.3, 38.0):
            via(d, x, y, '#2f72c4', '#0e2c55', r=0.3)

    # ---- os oito resistores (0805 em pe) e o C2 ----
    for x in (19.5, 21.69, 23.8, 25.81, 28.0, 30.1, 32.25, 34.38):
        d.ret_c(x, 40.18, 1.3, 1.05, ESTANHO, rx=0.08)
        d.ret_c(x, 42.27, 1.3, 1.05, ESTANHO, rx=0.08)
        chip_smd(d, x, 41.22, 1.25, 2.0, cor='#1d1d20', ilhas=False)
    for x in (43.97, 45.97):
        d.ret_c(x, 28.52, 1.1, 1.4, ESTANHO, rx=0.08)
    chip_smd(d, 44.97, 28.52, 2.0, 1.25, cor='#c9a77a', ilhas=False)

    # ---- os dois BTS7960B (TO-263-7): a aba estanhada em cima, as 7 pernas embaixo ----
    for x0 in (14.4, 25.2):
        cx = x0 + 4.975
        d.ret(x0, 20.05, 9.95, 10.3, ESTANHO, rx=0.1)
        for k in range(7):
            x = cx + (k - 3) * 1.25
            d.ret_c(x, 36.35, 1.0, 3.6, ESTANHO, rx=0.08)
            d.ret(x - 0.3, 30.2, 0.6, 6.3, 'url(#metal-h)', rx=0.08)
        for k in range(5):
            d.circ(x0 + 1.6 + k * 1.7, 20.85, 0.28, '#6b7178')
        E.sombra(d, x0 + 0.05, 21.8, 9.85, 8.5, rx=0.25)
        d.ret(x0 + 0.05, 21.8, 9.85, 8.5, '#232428', rx=0.25)
        d.ret(x0 + 0.05, 21.8, 9.85, 8.5, 'url(#luz)', rx=0.25)
        d.texto(cx - 0.6, 23.3, 'BTS7960B', 1.0, MARCA_CI, alinhar='center', peso='normal')
        d.circ(cx, 26.3, 1.25, '#3b3d42')
        d.texto(cx, 26.3, 'B2Z' if x0 < 20 else 'JE', 0.7, '#1a1b1e', alinhar='center', peso='bold')

    # ---- o 74HC244D (SOIC-20W): o pino 1 embaixo a esquerda ----
    for k in range(10):
        x = 36.78 + k * 1.26
        for y in (32.05, 41.2):
            d.ret_c(x, y, 0.55, 2.25, ESTANHO, rx=0.06)
        d.ret_c(x, 31.9, 0.42, 1.9, 'url(#metal-h)', rx=0.05)
        d.ret_c(x, 41.3, 0.42, 1.9, 'url(#metal-h)', rx=0.05)
    E.sombra(d, 36.05, 32.85, 12.8, 7.5, rx=0.2)
    d.ret(36.05, 32.85, 12.8, 7.5, PRETO_CI, rx=0.2)
    d.ret(36.05, 32.85, 12.8, 7.5, 'url(#luz)', rx=0.2)
    d.circ(37.0, 39.4, 0.38, '#3a3c41')
    for k, (t, tam) in enumerate((('74HC244D', 1.0), ('L3C1D4  10', 0.8), ('UXD1524K', 0.8))):
        d.texto(42.9, 34.7 + k * 1.45, t, tam, MARCA_CI, alinhar='center', peso='normal')

    # ---- o C1: o eletrolitico de 330 uF / 50 V em pe (o topo da lata, a faixa do - a esquerda) ----
    cx, cy, r = 14.32, 5.32, 4.95
    d.circ(cx + 0.2, cy + 0.25, r, '#000000', op=0.35)
    d.circ(cx, cy, r, '#16171a')
    d.caminho([('M', cx, cy - r), ('A', r, 0, 0, cx, cy + r), ('L', cx, cy + r - 0.6), ('A', r - 0.6, 0, 1, cx, cy - r + 0.6),
               ('Z',)], '#8e949b')
    d.circ(cx, cy, r - 0.6, 'url(#lata-topo)', stroke='#6d737b', sw=0.1)
    d.circ(cx, cy, r - 1.0, None, '#8a9097', 0.12)
    for ang in (0, 90):
        d.entrar(mover(cx, cy) * rodar(ang + 45))
        d.linha(-2.2, 0, 2.2, 0, '#7d838a', 0.22)
        d.sair()

    # ---- o borne P2 (dois KF301 verdes): B-, B+ | M+, M- ----
    xs = [23.24, 28.32, 33.40, 38.48]
    borne_kf301_verde(d, 20.7, 30.86, 0.0, 7.6, xs[:2], 3.65)
    borne_kf301_verde(d, 30.86, 41.02, 0.0, 7.6, xs[2:], 3.65)

    # ---- a barra P3 (2 x 4, os pinos para cima) ----
    E.sombra(d, 29.21, 44.18, 10.16, 5.08, rx=0.2)
    d.ret(29.21, 44.18, 10.16, 5.08, 'url(#plastico)', rx=0.2, stroke='#0a0a0b', sw=0.08)
    colunas = [38.1, 35.56, 33.02, 30.48]
    for x in colunas:
        for y in (45.45, 47.99):
            d.ret_c(x, y, 1.6, 1.6, '#202226', rx=0.3)
            d.ret_c(x, y, 0.84, 0.84, '#7a5c18')
            d.ret_c(x, y, 0.64, 0.64, 'url(#ouro)')

    pinos = [('B-', xs[0], 3.65), ('B+', xs[1], 3.65), ('M+', xs[2], 3.65), ('M-', xs[3], 3.65)]
    for (a, b), x in zip((('RPWM', 'LPWM'), ('R_EN', 'L_EN'), ('R_IS', 'L_IS'), ('VCC', 'GND')), colunas):
        pinos += [(a, x, 45.45), (b, x, 47.99)]
    gravar('bts7960', d, L, H)
    guardar('bts7960', L, H, pinos)


bts7960()


# =====================================================================
#  L9110S (o HG7881: 29,3 x 23 mm)
# =====================================================================
def borne_kf301_v(d, x0, x1, y0, y1, ys, x_parafuso, x_furo, cores):
    """O KF301 de 2 vias deitado no quadro da foto do L9110S: a boca dos fios
    virada para a direita (x1), os parafusos e os furinhos de teste."""
    corpo, escuro, borda = cores
    E.sombra(d, x0, y0, x1 - x0, y1 - y0, rx=0.4, op=0.35)
    d.ret(x0, y0, x1 - x0, y1 - y0, corpo, rx=0.35, stroke=borda, sw=0.12)
    d.ret(x1 - 1.1, y0 + 0.1, 1.1, y1 - y0 - 0.2, escuro, rx=0.25)                # o degrau da boca dos fios
    d.ret(x0, y0, x1 - x0, y1 - y0, 'url(#luz)', rx=0.35)
    ym = (y0 + y1) / 2
    d.linha(x0 + 2.6, ym, x1 - 1.1, ym, '#173f28', 0.55)                           # a divisoria
    for y in ys:
        d.circ(x_furo, y, 0.5, '#173f28')
        parafuso_kf(d, x_parafuso, y)


def l9110s():
    """Pela foto de cima da SunFounder (retificada em 40 px/mm pelos quatro
    furos; as anotacoes vermelhas tiradas por inpaint): o cobre vetorizado, os
    dois L9110S em SOIC-8, os resistores de 10k (103) e 1k (102), os
    capacitores, o LED, os bornes MOTOR B/MOTOR A e a barra de 6 pinos. A foto
    tem a barra a esquerda e os bornes a direita: o desenho e feito nela e
    girado 90 graus (na peca, os bornes em cima e a barra embaixo, o MOTOR B a
    esquerda, como na placa vista do lado da barra)."""
    Wf, Hf = 29.3, 23.0                       # o quadro da foto (deitado)
    L, H = E.caixa(Hf, Wf)
    d = E.novo()
    mascara, borda, cobre = '#234979', '#142c4c', '#2d5c99'
    d.ret(0.15, 0.2, Hf, Wf, '#000000', rx=0.3, op=0.3)
    d.ret(0, 0, Hf, Wf, mascara, rx=0.3, stroke=borda, sw=0.15)
    # o quadro da foto -> o da peca: (x, y) -> (y, Wf - x)
    d.entrar(T(0, -1, 1, 0, 0, Wf))
    cobre_da_foto(d, cobre, 'l9110s', lambda u, v: (u, v))
    d.ret(0, 0, Wf, Hf, 'url(#luz)', rx=0.3)
    S, sw = SERIGRAFIA, 0.2

    def caixa_silk(cx, cy, w, h, vertical):
        """A caixa da serigrafia em volta do passivo, aberta no meio dos lados compridos."""
        x0, x1, y0, y1 = cx - w / 2, cx + w / 2, cy - h / 2, cy + h / 2
        if vertical:
            d.linha(x0, y0, x1, y0, S, sw, cap='square')
            d.linha(x0, y1, x1, y1, S, sw, cap='square')
            for x in (x0, x1):
                d.linha(x, y0, x, cy - h * 0.18, S, sw, cap='square')
                d.linha(x, cy + h * 0.18, x, y1, S, sw, cap='square')
        else:
            d.linha(x0, y0, x0, y1, S, sw, cap='square')
            d.linha(x1, y0, x1, y1, S, sw, cap='square')
            for y in (y0, y1):
                d.linha(x0, y, cx - w * 0.18, y, S, sw, cap='square')
                d.linha(cx + w * 0.18, y, x1, y, S, sw, cap='square')
    resistores = [(5.32, 7.45, '103'), (8.4, 8.35, '103'), (5.35, 14.4, '103'), (8.4, 13.85, '103')]
    for cx, cy, _ in resistores:
        caixa_silk(cx, cy, 2.15, 5.0, True)
    for cy in (7.45, 15.1):
        caixa_silk(18.35, cy, 2.4, 5.1, True)
    for cy, h in ((7.5, 2.25), (9.65, 2.1), (11.95, 2.2), (14.4, 2.25)):
        caixa_silk(14.15, cy, 4.4, h, False)
    ys_barra = [11.0 + (k - 2.5) * 2.54 for k in range(6)]

    # ---- os furos (3,1 mm, sem anel) ----
    for x in (5.8, 19.4):
        for y in (2.3, 20.6):
            d.circ(x, y, 1.62, None, '#5a7fb0', 0.12)
            d.circ(x, y, 1.55, E.FURO)

    # ---- os passivos, o LED ----
    for k, (cx, cy, cod) in enumerate(resistores):
        chip_smd(d, cx, cy, 1.6, 3.2, cor='#1d1d20')
        texto_girado(d, cx, cy, cod, 0.75, '#e8e8e8', 180 if k == 2 else 0)
    for cy in (7.45, 15.1):
        chip_smd(d, 18.35, cy, 1.25, 3.2, cor='#a8825a')
    chip_smd(d, 14.1, 7.5, 3.2, 1.25, cor='#1d1d20')
    texto_girado(d, 14.1, 7.5, '102', 0.72, '#e8e8e8', 90)
    chip_smd(d, 14.1, 11.95, 3.2, 1.25, cor='#a8825a')
    chip_smd(d, 14.1, 14.4, 3.2, 1.6, cor='#9c7652')
    d.ret_c(14.5, 9.65, 2.0, 1.25, ESTANHO, rx=0.08)
    d.ret_c(14.5, 9.65, 1.3, 1.25, '#efece2')
    d.ret_c(14.5, 9.65, 0.75, 0.75, '#e6d8c4', rx=0.12)
    d.ret_c(14.5, 9.65, 0.3, 0.3, '#b49c74')

    # ---- os dois L9110S (SOIC-8): a marcacao lida de baixo para cima na foto ----
    for cy in (2.7, 19.85):
        for k in range(4):
            y = cy + (k - 1.5) * 1.27
            for s in (-1, 1):
                d.ret_c(12.7 + s * 3.15, y, 1.3, 0.62, ESTANHO, rx=0.06)
                d.ret_c(12.7 + s * 2.6, y, 1.3, 0.42, 'url(#metal)', rx=0.05)
        corpo_ci(d, 12.7, cy, 3.9, 4.9, [('L9110S', 0.75), ('345P', 0.65)], giro_texto=0, rx=0.15)
        # o texto: girado para ler de baixo para cima na foto (de cabeca para baixo na peca)
        d.partes = d.partes[:-2]
        texto_girado(d, 12.45, cy, 'L9110S', 0.75, MARCA_CI, 180)
        texto_girado(d, 13.45, cy, '345P' if cy < 10 else 'AU16P', 0.65, MARCA_CI, 180)
        d.circ(13.8, cy - 1.75, 0.22, '#3a3c41')

    # ---- a barra de 6 pinos (os pinos para cima) ----
    E.sombra(d, 0.23, ys_barra[0] - 1.27, 2.54, 15.24, rx=0.2)
    d.ret(0.23, ys_barra[0] - 1.27, 2.54, 15.24, 'url(#plastico)', rx=0.2, stroke='#0a0a0b', sw=0.08)
    for y in ys_barra:
        d.ret_c(1.5, y, 1.6, 1.6, '#202226', rx=0.3)
        d.ret_c(1.5, y, 0.84, 0.84, '#7a5c18')
        d.ret_c(1.5, y, 0.64, 0.64, 'url(#ouro)')

    # ---- os bornes: MOTOR B (em cima na foto) e MOTOR A ----
    verdes = ('#4fae74', '#3c8c5c', '#245a3b')
    borne_kf301_v(d, 22.0, 29.6, 0.0, 10.16, (2.54, 7.62), 26.3, 23.1, verdes)
    borne_kf301_v(d, 22.0, 29.6, 12.26, 22.42, (14.8, 19.88), 26.3, 23.1, verdes)
    d.sair()
    # ---- os rotulos, no quadro da peca (lidos como na placa: varios de cabeca para baixo) ----
    for t, x, y, g in (('MOTOR B', 7.55, 8.35, 0), ('MOTOR A', 15.1, 8.35, 0), ('C4', 10.9, 9.5, 180),
                       ('C3', 11.3, 10.6, 180), ('R1', 7.5, 18.1, 180), ('D1', 9.12, 17.6, 180),
                       ('C2', 10.43, 18.25, 180), ('C1', 13.03, 18.25, 180), ('R3', 15.2, 18.25, 180),
                       ('R4', 4.47, 21.1, 180), ('R5', 10.35, 23.8, 180), ('R2', 18.0, 24.7, 180),
                       ('B-1A', 3.7, 25.65, 0), ('B-1B', 7.0, 25.65, 0), ('GND', 10.2, 25.65, 0),
                       ('VCC', 12.8, 25.65, 0), ('A-1A', 15.5, 25.65, 0), ('A-1B', 19.0, 25.65, 180)):
        texto_girado(d, x, y, t, 0.85 if len(t) > 2 else 0.8, S, g)

    def Q(u, v):
        return round(v, 3), round(Wf - u, 3)
    pinos = [('B2', *Q(26.3, 2.54)), ('B1', *Q(26.3, 7.62)), ('A2', *Q(26.3, 14.8)), ('A1', *Q(26.3, 19.88))]
    for n, y in zip(['B-IA', 'B-IB', 'GND', 'VCC', 'A-IA', 'A-IB'], ys_barra):
        pinos.append((n, *Q(1.5, y)))
    gravar('l9110s', d, L, H)
    guardar('l9110s', L, H, pinos)
    luz('l9110s', *Q(14.5, 9.65), 0.75, '#ff3b30', 'energia')


l9110s()


# =====================================================================
#  ULN2003 (a placa verde SBT0811 do 28BYJ-48: 33,95 x 30,4 mm)
# =====================================================================
FAIXAS = {'preto': '#1c1c1c', 'marrom': '#7a3b1e', 'vermelho': '#c0392b', 'laranja': '#e67e22',
          'amarelo': '#e8c21a', 'verde': '#2e8b3e', 'azul': '#2c5fb8', 'violeta': '#7b3fa0', 'cinza': '#8a8a8a',
          'branco': '#f2f2f2', 'ouro': '#c9a23a'}


def resistor_axial_em_pe(d, x, y0, y1, yc, comp, diam, faixas):
    """O resistor axial deitado na vertical (o 1/8 W da placa): as ilhas, os
    terminais de aco e o corpo bege com as faixas (de cima para baixo)."""
    for y in (y0, y1):
        d.circ(x, y, 0.72, ESTANHO)
        d.circ(x, y, 0.72, 'url(#luz)')
    d.linha(x, y0, x, y1, '#b8bec5', 0.45, cap='round')
    d.linha(x - 0.08, y0, x - 0.08, y1, '#eef1f4', 0.12, cap='round')
    E.sombra(d, x - diam / 2, yc - comp / 2, diam, comp, rx=diam / 2)
    d.ret_c(x, yc, diam, comp, '#e3c9a0', rx=diam * 0.45, stroke='#b59b70', sw=0.06)
    d.ret_c(x, yc, diam * 1.08, comp * 0.24, '#e3c9a0', rx=0.3)
    for k, cor in enumerate(faixas):
        yy = yc - comp * 0.32 + k * comp * 0.2 if k < 3 else yc + comp * 0.36
        d.ret_c(x, yy, diam * (1.02 if k in (0, 3) else 0.98), 0.24, FAIXAS[cor])
    d.ret_c(x, yc, diam, comp, 'url(#metal-h)', rx=diam * 0.45, op=0.25)


def led_3mm(d, x, y):
    """O LED de 3 mm transparente visto de cima: a aba, a lente e o refletor."""
    d.circ(x + 0.15, y + 0.2, 1.9, '#000000', op=0.3)
    d.circ(x, y, 1.9, '#dfe6e8', stroke='#aab4b8', sw=0.1)
    d.circ(x, y, 1.5, '#f6f9fa', stroke='#c3cbcf', sw=0.08)
    d.ret_c(x - 0.35, y, 0.75, 0.55, '#a3a9ad', rx=0.1)          # o refletor (o catodo)
    d.ret_c(x + 0.35, y, 0.4, 0.3, '#b9bec2', rx=0.08)
    d.circ(x, y, 1.5, 'url(#cupula)')
    d.circ(x - 0.5, y - 0.55, 0.38, '#ffffff', op=0.8)


def uln2003():
    """Pela foto de cima da placa (a SBT0811, retificada pelos cantos com a
    escala do passo de 2,54 mm das ilhas IN5..IN7 e de 2,5 mm do conector XH:
    33,95 x 30,4 mm). A barra IN1..IN4 (e as ilhas vazias do IN5..IN7), o
    ULN2003APG no soquete DIP-16, o conector XH de 5 vias, os LEDs A..D com
    os resistores R5..R8 (470 ohms), o capacitor ceramico, a barra -/+ com o
    jumper do motor. O conector: o A em cima (o OUT1, ao lado do LED A) e o
    COM embaixo, do lado da alimentacao -- o plugue do 28BYJ-48 entra com o
    vermelho no COM, entao o azul cai no A e o laranja no D."""
    W, Hh = 33.95, 30.4
    L, H = E.caixa(W, Hh)
    d = E.novo()
    mascara, borda = '#1f9a3a', '#0f5a20'
    d.ret(0.15, 0.2, W, Hh, '#000000', rx=0.5, op=0.3)
    d.ret(0, 0, W, Hh, mascara, rx=0.5, stroke=borda, sw=0.15)
    d.ret(0, 0, W, Hh, 'url(#luz)', rx=0.5)
    S, sw = SERIGRAFIA, 0.22

    # ---- a serigrafia ----
    def sl(x1, y1, x2, y2):
        d.linha(x1, y1, x2, y2, S, sw, cap='square')
    sl(3.65, 3.65, 3.65, 29.95)
    sl(3.65, 3.65, 4.15, 3.65)
    sl(3.65, 6.2, 4.15, 6.2)
    sl(3.65, 13.95, 6.4, 13.95)
    sl(6.4, 13.95, 6.4, 29.95)
    sl(3.65, 20.85, 6.4, 20.85)
    sl(3.65, 29.95, 6.4, 29.95)
    for k, t in enumerate(['IN1', 'IN2', 'IN3', 'IN4', 'IN5', 'IN6', 'IN7']):
        d.texto(1.65, (5.9, 8.3, 10.65, 13.0, 15.55, 18.0, 20.3)[k], t, 1.2, S, alinhar='center', peso='bold')
    texto_girado(d, 6.25, 1.75, 'MCU', 1.05, S, 90)
    texto_girado(d, 5.1, 1.75, 'IO', 1.05, S, 90)
    for k, ch in enumerate('步进电机'):
        texto_girado(d, 4.98, 22.7 + k * 2.05, ch, 1.45, S, 90)
    d.texto(10.1, 29.35, '5-12V', 1.55, S, alinhar='center', peso='normal')
    d.texto(8.3, 28.35, '-', 1.0, S, alinhar='center', peso='bold')
    d.texto(10.75, 28.2, '+', 1.0, S, alinhar='center', peso='bold')
    d.texto(22.0, 1.2, 'ZC-A0591.', 1.25, S, alinhar='center', peso='normal')
    xs_led, ys_led = 27.4, [4.15 + k * 4.82 for k in range(4)]
    for t, y in zip('ABCD', ys_led):
        texto_girado(d, 24.6, y, t, 1.35, S, 90)
    for y in ys_led:
        d.circ(xs_led, y, 2.05, None, S, sw)
    texto_girado(d, 32.15, 16.0, 'SBT0811', 1.45, S, -90)
    texto_girado(d, 28.9, 25.6, 'X113647', 1.45, S, 90)
    for t, x in zip(['R5', 'R6', 'R7', 'R8'], (19.1, 21.6, 24.0, 26.4)):
        d.texto(x, 29.4, t, 1.3, S, alinhar='center', peso='normal')
    resistores = [(19.08, 22.01, 27.4, 24.65), (21.48, 22.01, 27.4, 24.65), (23.86, 21.96, 27.4, 24.35),
                  (26.22, 22.12, 27.35, 24.75)]
    for x, _, _, yc in resistores:
        d.ret(x - 1.05, yc - 1.35, 2.1, 2.75, None, stroke=S, sw=sw)
    d.ret(9.4, 23.6, 2.9, 0.65, None, stroke=S, sw=sw)

    # ---- os furos e as ilhas soltas ----
    for x in (2.1, 31.95):
        for y in (2.75, 27.46):
            d.circ(x, y, 1.45, E.FURO)
    ys_in = [4.93 + k * 2.54 for k in range(7)]
    for y in ys_in[4:]:
        d.circ(5.13, y, 0.72, ESTANHO)
        d.circ(5.13, y, 0.72, 'url(#luz)')
        d.circ(4.95, y - 0.2, 0.22, '#ffffff', op=0.6)
    for x, y, r in ((3.34, 23.19, 0.8), (20.56, 19.32, 0.7)):
        d.circ(x, y, r, ESTANHO)
        d.circ(x, y, r, 'url(#luz)')

    # ---- os resistores R5..R8 (470 ohms: amarelo, violeta, marrom, ouro) ----
    for k, (x, y0, y1, yc) in enumerate(resistores):
        faixas = ['amarelo', 'violeta', 'marrom', 'ouro']
        if k >= 2:
            faixas = ['marrom', 'violeta', 'amarelo', 'ouro']
        resistor_axial_em_pe(d, x, y0, y1, yc, 3.3, 1.75, faixas)

    # ---- o capacitor ceramico (o disco amarelo) ----
    for x in (8.0, 13.65):
        d.circ(x, 23.55, 0.75, ESTANHO)
        d.circ(x, 23.55, 0.75, 'url(#luz)')
    d.linha(8.0, 23.55, 13.65, 23.55, '#b8bec5', 0.4)
    d.caminho([('M', 8.9, 23.0), ('A', 2.05, 0, 1, 12.8, 23.0), ('A', 2.05, 0, 1, 8.9, 23.0), ('Z',)], None)
    d.partes.append(f'<ellipse cx="{pa.f(10.85 * A + 0.15 * A)}" cy="{pa.f(23.0 * A + 0.2 * A)}" rx="{pa.f(2.05 * A)}" '
                    f'ry="{pa.f(1.38 * A)}" fill="#000000" fill-opacity="0.3"/>\n')
    d.partes.append(f'<ellipse cx="{pa.f(10.85 * A)}" cy="{pa.f(23.0 * A)}" rx="{pa.f(2.05 * A)}" ry="{pa.f(1.38 * A)}" '
                    f'fill="#e9a640" stroke="#b97a22" stroke-width="{pa.f(0.08 * A)}"/>\n')
    d.partes.append(f'<ellipse cx="{pa.f(10.85 * A)}" cy="{pa.f(23.0 * A)}" rx="{pa.f(2.05 * A)}" ry="{pa.f(1.38 * A)}" '
                    f'fill="url(#cupula)"/>\n')

    # ---- o soquete DIP-16 e o ULN2003APG ----
    cx, cy = 12.0, 10.75
    E.sombra(d, 7.0, 0.9, 10.0, 19.7, rx=0.3)
    d.ret(7.0, 0.9, 10.0, 19.7, '#1a1b1e', rx=0.3, stroke='#0a0a0b', sw=0.08)
    d.ret(7.45, 1.35, 9.1, 18.8, None, rx=0.2, stroke='#2c2e33', sw=0.15)
    ys_dip = [cy + (k - 3.5) * 2.54 for k in range(8)]
    for y in ys_dip:
        for x in (7.65, 16.35):
            d.ret_c(x, y, 0.28, 1.1, '#b8913a', rx=0.05)
        for x0 in (7.95, 14.9):
            d.ret(x0, y - 0.65, 1.15, 1.3, 'url(#metal)', rx=0.08)
    E.sombra(d, 8.85, 1.2, 6.3, 19.1, rx=0.25)
    d.ret(8.85, 1.2, 6.3, 19.1, '#2b2c31', rx=0.25)
    d.ret(8.85, 1.2, 6.3, 19.1, 'url(#luz)', rx=0.25)
    d.caminho([('M', cx - 0.65, 1.2), ('A', 0.65, 0, 0, cx + 0.65, 1.2), ('Z',)], '#151619')
    texto_girado(d, 11.3, 11.4, 'ULN2003APG', 1.2, MARCA_CI, 90)
    texto_girado(d, 12.85, 12.6, 'CHINA  DN711F', 1.0, MARCA_CI, 90)

    # ---- o conector XH de 5 vias (o plugue do motor) ----
    ys_xh = [9.0 + (k - 2) * 2.5 for k in range(5)]
    E.sombra(d, 17.4, 2.65, 5.75, 12.7, rx=0.25)
    d.ret(17.4, 2.65, 5.75, 12.7, '#f1efe8', rx=0.25, stroke='#c9c4b6', sw=0.1)
    d.ret(17.95, 3.15, 3.95, 11.7, '#e3e0d6', rx=0.15, stroke='#cfcabd', sw=0.08)
    d.ret(22.45, 3.4, 0.45, 11.2, '#d9d5c9', rx=0.1)
    for y in ys_xh:
        d.ret_c(20.65, y, 0.75, 0.75, '#3a3227', rx=0.05)
        d.ret_c(20.65, y, 0.5, 0.5, 'url(#ouro)')
        d.linha(22.45, y + 1.25, 22.9, y + 1.25, '#c9c4b6', 0.1) if y < ys_xh[-1] else None
    d.ret(17.4, 2.65, 5.75, 12.7, 'url(#luz)', rx=0.25)

    # ---- os LEDs A..D ----
    for y in ys_led:
        led_3mm(d, xs_led, y)

    # ---- as barras: IN1..IN4 e a da alimentacao (-, + e o jumper do motor) ----
    E.sombra(d, 3.86, ys_in[0] - 1.27, 2.54, 10.16, rx=0.15)
    for y in ys_in[:4]:
        pino_jumper(d, 5.13, y)
    xs_p = [8.39 + k * 2.54 for k in range(4)]
    E.sombra(d, xs_p[0] - 1.27, 25.29, 10.16, 2.54, rx=0.15)
    for x in xs_p:
        pino_jumper(d, x, 26.56)
    capacete(d, (xs_p[2] + xs_p[3]) / 2, 26.56, 5.0, 2.5, True)

    pinos = [(f'IN{k + 1}', 5.13, ys_in[k]) for k in range(4)]
    pinos += [('-', xs_p[0], 26.56), ('+', xs_p[1], 26.56)]
    pinos += [(n, 20.65, y) for n, y in zip(['A', 'B', 'C', 'D', 'COM'], ys_xh)]
    gravar('uln2003', d, L, H)
    guardar('uln2003', L, H, pinos, {
        'IN1': ([], 'Entrada 1: HIGH liga a saida A (o OUT1) e o LED A'),
        'IN2': ([], 'Entrada 2: a saida B (o OUT2) e o LED B'),
        'IN3': ([], 'Entrada 3: a saida C (o OUT3) e o LED C'),
        'IN4': ([], 'Entrada 4: a saida D (o OUT4) e o LED D'),
        'A': ([], 'Conector do motor, o OUT1 (o IN1): no plugue do 28BYJ-48 cai o fio azul'),
        'B': ([], 'Conector do motor, o OUT2 (o IN2): o fio rosa'),
        'C': ([], 'Conector do motor, o OUT3 (o IN3): o fio amarelo'),
        'D': ([], 'Conector do motor, o OUT4 (o IN4): o fio laranja'),
        'COM': ([], 'Conector do motor: o fio vermelho (o comum, ligado no + da placa pelo jumper)')})
    for t, y in zip('ABCD', ys_led):
        luz('uln2003', xs_led, y, 1.5, '#ff3b30', t)


uln2003()


# =====================================================================
#  L293D (o DIP-16 da TI, o L293DNE: o mesmo desenho dos 74HC da Logica)
# =====================================================================
def l293d():
    """O PDIP-16 (N) da ficha da TI visto de cima: 19,3 mm de corpo, as
    fileiras a 7,62 mm, o chanfro e a bolinha do pino 1 embaixo a esquerda;
    a pinagem da ficha (os quatro GND do meio sao o dissipador)."""
    comp = 19.3
    L, H = E.caixa(comp + 0.6, 9.2)
    d = E.novo()
    xs = [L / 2 + (k - 3.5) * PASSO for k in range(8)]
    ys = [H / 2 - 7.62 / 2, H / 2 + 7.62 / 2]
    for x in xs:
        for y0, y1 in ((ys[0] - 0.45, H / 2 - 3.1), (H / 2 + 3.1, ys[1] + 0.45)):
            d.ret(x - 0.76, min(y0, y1), 1.52, abs(y1 - y0), 'url(#metal-h)', rx=0.12, stroke='#7c838b', sw=0.06)
    d.ret_c(L / 2, H / 2, comp, 6.35, '#1d1e21', rx=0.35, stroke='#08090a', sw=0.12)
    d.ret_c(L / 2, H / 2, comp, 6.35, 'url(#luz)', rx=0.35)
    xe = L / 2 - comp / 2
    d.caminho([('M', xe, H / 2 - 1.1), ('A', 1.1, 0, 1, xe, H / 2 + 1.1), ('Z',)], '#0d0e10')
    d.circ(xs[0], H / 2 + 1.75, 0.42, '#141517', stroke='#2c2e32', sw=0.08)
    d.texto(L / 2 + 0.6, H / 2 - 0.55, 'L293DNE', 1.05, '#c8cbd0', alinhar='center')
    d.texto(L / 2 + 0.6, H / 2 + 1.05, '08A3EKM', 0.7, '#8d9197', alinhar='center', peso='normal')
    baixo = ['1,2EN', '1A', '1Y', 'GND.1', 'GND.2', '2Y', '2A', 'VCC2']
    cima = ['3,4EN', '3A', '3Y', 'GND.3', 'GND.4', '4Y', '4A', 'VCC1']
    pinos = [(n, x, ys[1]) for n, x in zip(baixo, xs)] + [(n, x, ys[0]) for n, x in zip(cima, reversed(xs))]
    gravar('l293d', d, L, H)
    guardar('l293d', L, H, pinos)


l293d()


# =====================================================================
if __name__ == '__main__':
    cam = pa.BASE + 'app/assets/pecas.json'
    dados = json.load(io.open(cam, encoding='utf-8'))
    novos = {p['id'] for p in pecas}
    dados = [p for p in dados if p['id'] not in novos] + pecas
    dados.sort(key=lambda p: p['id'])
    io.open(cam, 'w', encoding='utf-8', newline='').write(json.dumps(dados, ensure_ascii=False, indent=1))
    print('luzes (EditorModulos.LuzesDosModulos):')
    for p, x, y, r, cor, chave in luzes:
        print(f'  {p}: ({x:.2f}, {y:.2f}, {r:.2f}, {cor}, "{chave}")')
    print(f'catalogo: {len(dados)} pecas')
