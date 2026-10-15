"""python ferramentas/extrair-svg/pecas-passivos.py

Os passivos redesenhados (Alpha 25), no estilo das placas e dos LEDs (as
primitivas e a paleta de placas-arduino.py), em escala real (96 px por
polegada) e com o desenho seguindo o modelo escolhido na peca:

  - resistor.svg: o de 1/4 W de filme de carbono (6,3 x 2,4 mm, o "osso"
    bege); as faixas do valor sao desenhadas por cima pelo editor, no lugar
    que sai daqui (app/DesenhoDosPassivos.cs);
  - capacitor-<modelo>.svg: o ceramico de disco com o codigo (10 pF a 100 nF)
    e o multicamada de 1 uF;
  - capacitor-eletrolitico-<modelo>.svg: a lata de cada valor (5 x 11 mm do
    1 uF ate 10 x 20 mm do 2200 uF), a capa com a faixa do negativo e o valor;
  - indutor-<modelo>.svg: o de nucleo de tambor (8 x 10 mm), o fio mais grosso
    nos de poucos uH (mais corrente) e mais fino nos de mH;
  - cristal-<modelo>.svg: o HC-49/S com a frequencia, e o cilindro de 2 x 6 mm
    do 32,768 kHz;
  - ldr.svg: o GL5528 de 5 mm, com a trilha de CdS em zigue-zague;
  - fusivel-<modelo>.svg: o de vidro de 3,6 x 10 mm com o filamento (mais
    grosso quanto maior a corrente) e o ceramico de 5 x 20 mm do de 10 A;
  - varistor-<modelo>.svg: o disco 14D (15,5 mm), azul.

Os pinos de cada peca nao mudam. O que passa da caixa (a lata grande, o disco
do varistor) vai na sobra do SVG (data-sobra): o editor desenha para fora,
sem mexer nos pinos.
"""
import importlib.util
import io
import json
import os

AQUI = os.path.dirname(os.path.abspath(__file__))
_spec = importlib.util.spec_from_file_location('placas_arduino', os.path.join(AQUI, 'placas-arduino.py'))
pa = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(pa)

Desenho, T, mover, rodar, f = pa.Desenho, pa.T, pa.mover, pa.rodar, pa.f
BASE = pa.BASE
A = 96 / 25.4

PECAS = {p['id']: p for p in json.load(io.open(BASE + 'app/assets/pecas.json', encoding='utf-8'))}


def pinos(tipo):
    """Os pinos da peca, em mm."""
    return {q['nome']: (q['x'] / A, q['y'] / A) for q in PECAS[tipo]['pinos']}


def caixa(tipo):
    return PECAS[tipo]['largura'], PECAS[tipo]['altura']


DEFS = pa.DEFS + '''  <linearGradient id="cilindro" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#000000" stop-opacity="0.28"/><stop offset="0.28" stop-color="#ffffff" stop-opacity="0.22"/><stop offset="0.42" stop-color="#ffffff" stop-opacity="0.05"/><stop offset="0.75" stop-color="#000000" stop-opacity="0.08"/><stop offset="1" stop-color="#000000" stop-opacity="0.35"/></linearGradient>
  <linearGradient id="cilindro-v" x1="0" y1="0" x2="1" y2="0"><stop offset="0" stop-color="#000000" stop-opacity="0.35"/><stop offset="0.22" stop-color="#ffffff" stop-opacity="0.22"/><stop offset="0.38" stop-color="#ffffff" stop-opacity="0.04"/><stop offset="0.8" stop-color="#000000" stop-opacity="0.1"/><stop offset="1" stop-color="#000000" stop-opacity="0.4"/></linearGradient>
  <radialGradient id="disco" cx="0.38" cy="0.32" r="0.75"><stop offset="0" stop-color="#ffffff" stop-opacity="0.35"/><stop offset="0.45" stop-color="#ffffff" stop-opacity="0"/><stop offset="1" stop-color="#000000" stop-opacity="0.3"/></radialGradient>
  <linearGradient id="cobre" x1="0" y1="0" x2="1" y2="0"><stop offset="0" stop-color="#6e3b14"/><stop offset="0.25" stop-color="#d98c4a"/><stop offset="0.45" stop-color="#f0b27a"/><stop offset="0.75" stop-color="#b8692e"/><stop offset="1" stop-color="#5e3110"/></linearGradient>
  <linearGradient id="ferrite" x1="0" y1="0" x2="1" y2="0"><stop offset="0" stop-color="#1c1d20"/><stop offset="0.3" stop-color="#55575c"/><stop offset="0.6" stop-color="#3a3c40"/><stop offset="1" stop-color="#16171a"/></linearGradient>
  <linearGradient id="vidro" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#d6e4ec" stop-opacity="0.5"/><stop offset="0.25" stop-color="#ffffff" stop-opacity="0.3"/><stop offset="0.55" stop-color="#dfe9ef" stop-opacity="0.08"/><stop offset="1" stop-color="#9fb4c0" stop-opacity="0.4"/></linearGradient>
'''


def documento(tipo, sx, sy, partes, nome):
    L, H = caixa(tipo)
    W, Hs = L + 2 * sx, H + 2 * sy
    sobra = f' data-sobra="{f(sx)} {f(sy)}"' if sx or sy else ''
    svg = (f'<svg xmlns="http://www.w3.org/2000/svg" version="1.1" width="{f(W)}px" height="{f(Hs)}px"{sobra} '
           f'viewBox="0 0 {f(W)} {f(Hs)}">\n<defs>\n{DEFS}</defs>\n' + ''.join(partes) + '</svg>\n')
    io.open(BASE + f'app/assets/{nome}.svg', 'w', encoding='utf-8', newline='').write(svg)
    print(f'  {nome}.svg: {len(svg) // 1024 + 1} KB' + (f', sobra {sx:.1f} x {sy:.1f} px' if sx or sy else ''))


def desenho(sx=0.0, sy=0.0):
    """mm com y para baixo, a origem no canto da caixa (a sobra empurra tudo)."""
    return Desenho(T(A, 0, 0, A, sx, sy))


def sobra_de(tipo, x0, y0, x1, y1):
    """A sobra (px, igual dos dois lados) para caber a arte de (x0, y0) a (x1, y1) mm."""
    L, H = caixa(tipo)
    sx = max(0.0, -x0 * A, x1 * A - L)
    sy = max(0.0, -y0 * A, y1 * A - H)
    return round(sx + (0.6 if sx else 0), 1), round(sy + (0.6 if sy else 0), 1)


def perna(d, pontos, larg=0.55):
    """Uma perna estanhada: a linha grossa e o brilho do lado esquerdo."""
    cmds = [('M', *pontos[0])] + [('L', *p) for p in pontos[1:]]
    d.caminho(cmds, None, '#8e959c', larg, cap='butt')
    d.caminho([(c[0], c[1] - larg * 0.18, c[2]) for c in cmds], None, '#e9edf0', larg * 0.3, cap='butt', so=0.85)


# =====================================================================
#  o resistor
# =====================================================================
RES_L, RES_D, RES_CAPA = 6.3, 2.4, 1.55


def resistor_geom():
    p = pinos('resistor')
    (xa, ya), (xb, _) = p['1'], p['2']
    cx = (xa + xb) / 2
    return cx, ya, cx - RES_L / 2, cx + RES_L / 2


def faixas_do_resistor():
    """As quatro faixas (x do meio, largura, altura), em mm: tres juntas na
    ponta esquerda, a de tolerancia afastada na direita (assim se sabe por
    onde comecar a ler)."""
    _, _, x0, x1 = resistor_geom()
    return [(x0 + 0.95, 0.5, RES_D - 0.06), (x0 + 1.75, 0.5, RES_D - 0.22), (x0 + 2.55, 0.5, RES_D * 0.86),
            (x1 - 0.95, 0.5, RES_D - 0.06)]


def gerar_resistor():
    cx, cy, x0, x1 = resistor_geom()
    d = desenho()
    L, _ = caixa('resistor')
    perna(d, [(0, cy), (x0 + 0.5, cy)], 0.6)
    perna(d, [(x1 - 0.5, cy), (L / A, cy)], 0.6)
    bege, sombra = '#d9b98b', '#a9875c'
    meio = RES_D * 0.86
    # o osso: o meio mais fino e as duas tampas mais gordas
    d.ret(x0 + RES_CAPA - 0.4, cy - meio / 2, RES_L - 2 * RES_CAPA + 0.8, meio, bege)
    for xa in (x0, x1 - RES_CAPA):
        d.ret(xa, cy - RES_D / 2, RES_CAPA, RES_D, bege, rx=RES_D * 0.45)
    d.ret(x0 + RES_CAPA - 0.4, cy - meio / 2, RES_L - 2 * RES_CAPA + 0.8, meio, 'url(#cilindro)')
    for xa in (x0, x1 - RES_CAPA):
        d.ret(xa, cy - RES_D / 2, RES_CAPA, RES_D, 'url(#cilindro)', rx=RES_D * 0.45)
    # a laca escorrida na emenda das tampas
    for xa in (x0 + RES_CAPA - 0.25, x1 - RES_CAPA + 0.25):
        d.linha(xa, cy - meio / 2 + 0.05, xa, cy + meio / 2 - 0.05, sombra, 0.12, so=0.5)
    documento('resistor', 0, 0, d.partes, 'resistor')


# =====================================================================
#  o capacitor ceramico
# =====================================================================
CERAMICOS = {
    #        diametro, cor, codigo
    '10p': (3.8, '#e7a24a', '10'), '22p': (3.8, '#e7a24a', '22'), '100p': (4.0, '#e59a44', '101'),
    '1n': (4.5, '#df8a3b', '102'), '10n': (5.0, '#d97a31', '103'), '100n': (5.5, '#cf6a26', '104'),
    '1u': (None, '#e3b23c', '105'),
}


def gerar_ceramico(modelo):
    diam, cor, codigo = CERAMICOS[modelo]
    p = pinos('capacitor')
    (xa, yp), (xb, _) = p['1'], p['2']
    cx = (xa + xb) / 2
    d = desenho()
    if diam:                                     # o disco
        r = diam / 2
        cy = 5.6 - r
        for x in (xa, xb):
            perna(d, [(cx + (x - cx) * 0.75, cy + r * 0.4), (x, cy + r + 0.9), (x, yp + 0.3)], 0.5)
        # o banho de resina escorrendo nas pernas
        yb = cy + r
        for x in (xa, xb):
            xs = cx + (x - cx) * 0.8
            d.caminho([('M', xs - 0.55, yb - 0.6), ('L', x - 0.32, yb + 0.85), ('A', 0.32, 0, 0, x + 0.32, yb + 0.85),
                       ('L', xs + 0.55, yb - 0.6), ('Z',)], cor)
        d.circ(cx, cy, r, cor, '#8a4515', 0.06)
        d.circ(cx, cy, r, 'url(#disco)')
        d.texto(cx, cy, codigo, diam * 0.24, '#3a1d08', ancora='middle', alinhar='center')
    else:                                        # o multicamada de 1 uF, o "bloquinho" amarelo
        w, h = 4.2, 3.6
        cy = 5.6 - h / 2
        for x in (xa, xb):
            perna(d, [(x, cy + h / 2 - 0.4), (x, yp + 0.3)], 0.5)
        d.ret_c(cx, cy, w, h, cor, rx=1.3, stroke='#9a7418', sw=0.06)
        d.ret_c(cx, cy, w, h, 'url(#disco)', rx=1.3)
        d.texto(cx, cy, codigo, 1.05, '#3b2b05', ancora='middle', alinhar='center')
    documento('capacitor', 0, 0, d.partes, f'capacitor-{modelo}')


# =====================================================================
#  o eletrolitico
# =====================================================================
ELETROLITICOS = {
    #              D,    L,   passo, texto
    '1u-50v': (5.0, 11.0, 2.0, ('1µF', '50V')), '10u-25v': (5.0, 11.0, 2.0, ('10µF', '25V')),
    '47u-25v': (5.0, 11.0, 2.0, ('47µF', '25V')), '100u-25v': (6.3, 11.0, 2.5, ('100µF', '25V')),
    '220u-16v': (6.3, 11.0, 2.5, ('220µF', '16V')), '470u-16v': (8.0, 11.5, 3.5, ('470µF', '16V')),
    '1000u-16v': (10.0, 16.0, 5.0, ('1000µF', '16V')), '2200u-16v': (10.0, 20.0, 5.0, ('2200µF', '16V')),
}


def gerar_eletrolitico(modelo):
    D, L, passo, (valor, volts) = ELETROLITICOS[modelo]
    p = pinos('capacitor-eletrolitico')
    (xm, yp), (xn, _) = p['+'], p['-']
    cx = (xm + xn) / 2
    yb = yp - 2.6                  # o fundo da lata (a borracha)
    yt = yb - L
    sx, sy = sobra_de('capacitor-eletrolitico', cx - D / 2 - 0.1, yt - 0.1, cx + D / 2 + 0.1, yp + 0.3)
    d = desenho(sx, sy)
    # as pernas: saem no passo da lata e vao ao furo
    for x, xs in ((xm, cx - passo / 2), (xn, cx + passo / 2)):
        perna(d, [(xs, yb - 0.2), (xs, yb + 0.6), (x, yb + 1.6), (x, yp + 0.3)], 0.55)
    d.ret_c(cx, yb + 0.05, D * 0.82, 0.5, '#3a3b3f', rx=0.2)                      # a borracha
    d.ret(cx - D / 2, yt + 0.25, D, L - 0.25, '#1d1e22', rx=0.35)                 # a capa
    d.ret(cx + D / 2 - D * 0.27, yt + 0.25, D * 0.27, L - 0.25, '#8f949b')          # a faixa do negativo
    for k in range(int((L - 1.6) / 1.7)):
        yy = yt + 1.4 + k * 1.7
        d.ret_c(cx + D / 2 - D * 0.135, yy, D * 0.12, 0.22, '#1d1e22')
    d.ret(cx - D / 2, yt + 0.25, D, L - 0.25, 'url(#cilindro-v)', rx=0.35)
    d.ret(cx - D / 2 + 0.05, yt, D - 0.1, 0.55, 'url(#metal)', rx=0.25)          # a borda de aluminio em cima
    d.linha(cx - D / 2 + 0.1, yb - 1.25, cx + D / 2 - 0.1, yb - 1.25, '#0d0d0f', 0.18, so=0.85)   # o friso
    d.linha(cx - D / 2 + 0.1, yb - 1.1, cx + D / 2 - 0.1, yb - 1.1, '#4a4b50', 0.08, so=0.7)
    tam = min(0.95, D * 0.15)
    xt = cx - D * 0.1
    d.texto(xt, yt + L * 0.48, f'{valor} {volts}', tam, '#e8e6e0', ang=90, ancora='middle', alinhar='center')
    if L >= 11:
        d.texto(xt - tam * 1.35, yt + L * 0.48, '105°C', tam * 0.75, '#c9c6bf', ang=90, ancora='middle', alinhar='center',
                peso='normal')
    documento('capacitor-eletrolitico', sx, sy, d.partes, f'capacitor-eletrolitico-{modelo}')


# =====================================================================
#  o indutor de tambor
# =====================================================================
INDUTORES = {'10uh': 0.85, '22uh': 0.75, '47uh': 0.62, '100uh': 0.5, '330uh': 0.36, '1mh': 0.22, '10mh': 0.11}


def gerar_indutor(modelo):
    fio = INDUTORES[modelo]
    p = pinos('indutor')
    (xa, yp), (xb, _) = p['1'], p['2']
    cx = (xa + xb) / 2
    D, H = 8.0, 10.0
    yb = yp - 1.4
    yt = yb - H
    sx, sy = sobra_de('indutor', cx - D / 2 - 0.1, yt, cx + D / 2 + 0.1, yp)
    d = desenho(sx, sy)
    for x in (xa, xb):
        perna(d, [(x, yb - 0.5), (x, yp + 0.3)], 0.6)
    # o enrolamento entre as abas do tambor
    w = D - 0.5
    d.ret(cx - w / 2, yt + 0.9, w, H - 1.8, 'url(#cobre)', rx=0.3)
    n = int((H - 1.8) / fio)
    for k in range(n + 1):
        yy = yt + 0.9 + k * (H - 1.8) / max(n, 1)
        d.linha(cx - w / 2 + 0.1, yy, cx + w / 2 - 0.1, yy, '#5a2c0c', min(0.09, fio * 0.25), so=0.65)
        if fio >= 0.3:
            d.linha(cx - w / 2 + 0.3, yy + fio * 0.32, cx + w / 2 - 0.3, yy + fio * 0.32, '#ffd2a6', fio * 0.16, so=0.45)
    d.ret(cx - w / 2, yt + 0.9, w, H - 1.8, 'url(#cilindro-v)', rx=0.3)
    # as abas de ferrite, em cima e embaixo
    for y in (yt, yb - 1.0):
        d.ret(cx - D / 2, y, D, 1.0, 'url(#ferrite)', rx=0.25, stroke='#0c0d0f', sw=0.05)
    documento('indutor', sx, sy, d.partes, f'indutor-{modelo}')


# =====================================================================
#  o cristal
# =====================================================================
CRISTAIS = {'16mhz': '16.000', '8mhz': '8.000', '12mhz': '12.000', '32khz': None}


def gerar_cristal(modelo):
    marca = CRISTAIS[modelo]
    p = pinos('cristal')
    (xa, yp), (xb, _) = p['1'], p['2']
    cx = (xa + xb) / 2
    d = desenho()
    if marca:                                    # o HC-49/S
        yb = yp - 3.4
        for x in (xa, xb):
            perna(d, [(x, yb - 0.3), (x, yp + 0.3)], 0.45)
        d.ret_c(cx, yb - 0.3, 11.35, 0.6, '#3d3f44', rx=0.15)                      # a base
        d.ret(cx - 11.05 / 2, yb - 0.6 - 3.5, 11.05, 3.5, 'url(#metal)', rx=0.7, stroke='#7c838b', sw=0.06)
        d.ret(cx - 11.05 / 2 + 0.35, yb - 0.6 - 3.5 + 0.35, 11.05 - 0.7, 3.5 - 0.7, None, rx=0.5, stroke='#d5d9de', sw=0.08)
        d.texto(cx, yb - 0.6 - 1.75, marca, 1.25, '#3a3e44', ancora='middle', alinhar='center')
    else:                                        # o de 32,768 kHz: o cilindro de 2 x 6 mm, em pe
        yb = 6.6
        for x, dx in ((xa, -0.35), (xb, 0.35)):
            perna(d, [(cx + dx, yb - 0.2), (cx + dx, yb + 0.5), (x, yb + 2.0), (x, yp + 0.3)], 0.3)
        d.ret(cx - 1.0, yb - 6.0, 2.0, 6.0, 'url(#metal-h)', rx=0.35, stroke='#6f767e', sw=0.05)
        d.ret_c(cx, yb - 0.25, 1.7, 0.5, '#2c2d31', rx=0.1)
    documento('cristal', 0, 0, d.partes, f'cristal-{modelo}')


# =====================================================================
#  o LDR (GL5528)
# =====================================================================
def gerar_ldr():
    p = pinos('ldr')
    (xa, yp), (xb, _) = p['1'], p['2']
    cx = (xa + xb) / 2
    r = 2.55
    cy = 3.0
    d = desenho()
    for x in (xa, xb):
        perna(d, [(cx + (x - cx) * 1.15, cy + r - 0.3), (x, cy + r + 1.0), (x, yp + 0.3)], 0.5)
    d.circ(cx, cy, r, '#efe9dd', '#b9b1a1', 0.08)                               # a ceramica
    d.circ(cx, cy, r - 0.45, '#cfcac0')                                         # os eletrodos (pente)
    # a trilha de CdS: o zigue-zague entre os dois pentes
    n, larg = 7, (r - 0.7) * 2
    pts = []
    for k in range(n):
        y = cy - (r - 0.95) + k * (2 * (r - 0.95)) / (n - 1)
        meia = ((r - 0.6) ** 2 - (y - cy) ** 2) ** 0.5 - 0.35
        pts += [(cx - meia, y), (cx + meia, y)] if k % 2 == 0 else [(cx + meia, y), (cx - meia, y)]
    d.poli(pts, None, '#c4542a', 0.42, fechado=False, cap='round')
    d.poli(pts, None, '#e07a4a', 0.14, fechado=False, cap='round')
    d.circ(cx, cy, r, 'url(#disco)')                                            # a resina por cima
    d.caminho([('M', cx - r * 0.6, cy - r * 0.45), ('A', r * 0.85, 0, 1, cx + r * 0.1, cy - r * 0.78)], None, '#ffffff',
              0.25, so=0.5, cap='round')
    documento('ldr', 0, 0, d.partes, 'ldr')


# =====================================================================
#  o fusivel
# =====================================================================
FUSIVEIS = {'500ma': (0.04, '500mA'), '1a': (0.06, '1A'), '2a': (0.09, '2A'), '5a': (0.16, '5A'), '10a': (None, 'F10A 250V')}


def gerar_fusivel(modelo):
    fio, marca = FUSIVEIS[modelo]
    p = pinos('fusivel')
    (xa, cy), (xb, _) = p['1'], p['2']
    cx = (xa + xb) / 2
    L_px, _ = caixa('fusivel')
    if fio:                                      # o de vidro, 3,6 x 10 mm
        comp, diam, tampa = 10.0, 3.6, 2.3
    else:                                        # o ceramico, 5 x 20 mm
        comp, diam, tampa = 20.0, 5.0, 4.5
    x0, x1 = cx - comp / 2, cx + comp / 2
    sx, sy = sobra_de('fusivel', min(x0 - 0.8, 0), cy - diam / 2 - 0.1, max(x1 + 0.8, L_px / A), cy + diam / 2 + 0.1)
    d = desenho(sx, sy)
    if fio:
        perna(d, [(xa, cy), (x0 + 0.5, cy)], 0.6)
        perna(d, [(x1 - 0.5, cy), (xb, cy)], 0.6)
    else:
        # o de 5 x 20 mm e mais comprido que o passo dos pinos: as pernas
        # dobram para baixo dele, e so a ponta delas aparece nas tampas
        perna(d, [(x0 - 0.6, cy), (x0 + 0.5, cy)], 0.6)
        perna(d, [(x1 - 0.5, cy), (x1 + 0.6, cy)], 0.6)
    if fio:
        d.ret(x0 + tampa - 0.3, cy - diam / 2 + 0.15, comp - 2 * tampa + 0.6, diam - 0.3, 'url(#vidro)', stroke='#9fb4c0', sw=0.05)
        g = max(fio * 2.2, 0.12)                                                  # o filamento
        d.linha(x0 + tampa - 0.2, cy, x1 - tampa + 0.2, cy, '#6b7178', g + 0.06)
        d.linha(x0 + tampa - 0.2, cy, x1 - tampa + 0.2, cy, '#dfe3e7', g)
        d.ret(x0 + tampa - 0.3, cy - diam / 2 + 0.3, comp - 2 * tampa + 0.6, (diam - 0.3) * 0.22, '#ffffff', op=0.35)
    else:
        d.ret(x0 + tampa - 0.3, cy - diam / 2 + 0.2, comp - 2 * tampa + 0.6, diam - 0.4, '#efe9dc', stroke='#b8b0a0', sw=0.05)
        d.ret(x0 + tampa - 0.3, cy - diam / 2 + 0.2, comp - 2 * tampa + 0.6, diam - 0.4, 'url(#cilindro)')
        d.texto(cx, cy, marca, 1.0, '#2c2a26', ancora='middle', alinhar='center')
    for xt in (x0, x1 - tampa):                  # as tampas niqueladas
        d.ret(xt, cy - diam / 2, tampa, diam, 'url(#metal)', rx=0.25, stroke='#7c838b', sw=0.05)
    if fio:
        d.texto(x0 + tampa / 2, cy, marca, 0.48 if len(marca) > 3 else 0.62, '#4a4f56', ancora='middle', alinhar='center',
                peso='normal')
    documento('fusivel', sx, sy, d.partes, f'fusivel-{modelo}')


# =====================================================================
#  o varistor 14D
# =====================================================================
VARISTORES = {'14d270k': ('14D270K', 4.3), '14d470k': ('14D470K', 4.4), '14d391k': ('14D391K', 5.6)}


def gerar_varistor(modelo):
    marca, espessura = VARISTORES[modelo]
    p = pinos('varistor')
    (xa, yp), (xb, _) = p['1'], p['2']
    cx = (xa + xb) / 2
    D = 15.5
    r = D / 2
    cy = yp - 2.4 - r
    sx, sy = sobra_de('varistor', cx - r - 0.1, cy - r - 0.1, cx + r + 0.1, yp + 0.3)
    d = desenho(sx, sy)
    for x, xs in ((xa, cx - 3.75), (xb, cx + 3.75)):
        perna(d, [(xs, cy + r * 0.6), (xs, cy + r + 0.6), (x, cy + r + 1.6), (x, yp + 0.3)], 0.65)
    d.caminho([('M', cx - 4.3, cy + r * 0.75), ('L', cx - 4.1, cy + r + 0.9), ('L', cx - 3.4, cy + r + 0.9),
               ('L', cx + 3.4, cy + r + 0.9), ('L', cx + 4.1, cy + r + 0.9), ('L', cx + 4.3, cy + r * 0.75), ('Z',)], '#1f5fb5')
    d.circ(cx, cy, r, '#1f5fb5', '#123c78', 0.08)
    d.circ(cx, cy, r, 'url(#disco)')
    d.texto(cx, cy - 0.8, marca, 1.6, '#e9eef6', ancora='middle', alinhar='center')
    d.texto(cx, cy + 1.5, 'MOV', 1.0, '#c9d6ea', ancora='middle', alinhar='center', peso='normal')
    documento('varistor', sx, sy, d.partes, f'varistor-{modelo}')


# =====================================================================
#  a geometria das faixas para o C#
# =====================================================================
def gerar_cs():
    cx, cy, x0, x1 = resistor_geom()
    linhas = [
        'namespace Simulador.App;',
        '',
        '// GERADO por ferramentas/extrair-svg/pecas-passivos.py -- nao edite a mao.',
        '//',
        '// Onde ficam as faixas do resistor (px da caixa): o editor pinta cada uma',
        '// com a cor do valor, por cima do corpo desenhado no resistor.svg.',
        'static class DesenhoDosPassivos',
        '{',
        '    /// <summary>O meio de cada faixa (x), a largura e a altura, com o centro em y.</summary>',
        '    public static readonly (double X, double Largura, double Altura)[] FaixasDoResistor =',
        '    [',
    ]
    for x, w, h in faixas_do_resistor():
        linhas.append(f'        ({x * A:.2f}, {w * A:.2f}, {h * A:.2f}),')
    linhas += ['    ];', '', f'    public const double CentroDasFaixas = {cy * A:.2f};', '}']
    io.open(BASE + 'app/DesenhoDosPassivos.cs', 'w', encoding='utf-8', newline='').write('\n'.join(linhas) + '\n')
    print('  app/DesenhoDosPassivos.cs')


def copiar(de, para):
    io.open(BASE + f'app/assets/{para}.svg', 'w', encoding='utf-8', newline='').write(
        io.open(BASE + f'app/assets/{de}.svg', encoding='utf-8').read())


if __name__ == '__main__':
    gerar_resistor()
    for m in CERAMICOS:
        gerar_ceramico(m)
    for m in ELETROLITICOS:
        gerar_eletrolitico(m)
    for m in INDUTORES:
        gerar_indutor(m)
    for m in CRISTAIS:
        gerar_cristal(m)
    gerar_ldr()
    for m in FUSIVEIS:
        gerar_fusivel(m)
    for m in VARISTORES:
        gerar_varistor(m)
    # o desenho do tipo (a paleta, a miniatura): o modelo de fabrica de cada um
    for tipo, padrao in (('capacitor', '100n'), ('capacitor-eletrolitico', '100u-25v'), ('indutor', '100uh'),
                         ('cristal', '16mhz'), ('fusivel', '500ma'), ('varistor', '14d270k')):
        copiar(f'{tipo}-{padrao}', tipo)
    gerar_cs()
