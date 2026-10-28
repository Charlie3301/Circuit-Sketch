"""python ferramentas/extrair-svg/pecas-entradas.py

As entradas da Alpha 25, no estilo das placas e no tamanho real:

  - pushbutton          : a chave tatil de 12 x 12 mm com a capa redonda (as
                          pernas no passo de 12,7 x 5,08 mm). A cor da capa e
                          o modelo da peca (o "color" do Wokwi).
  - pushbutton-6mm      : a chave tatil de 6 x 6 mm (as pernas de 6,5 x 4,5 mm
                          entram na protoboard em 7,62 x 5,08).
  - potentiometer       : o potenciometro de 16 mm (WH148, B10K) visto de
                          frente, com o knob e as tres pernas no passo de 5 mm.
  - slide-potentiometer : o deslizante de 30 mm de curso (Bourns PTA3043, a
                          ficha da Bourns: 45 x 9 mm, os pinos 1 e 2 numa ponta
                          e o 3 na outra, 43,5 mm entre eles) com o knob.
  - slide-switch        : a chave deslizante SS12D00G4 (alavanca de 4 mm), de
                          frente, com as tres pernas no passo de 2,54 mm.
  - dip-switch-8        : a chave DIP de 8 vias (0,3": as fileiras a 7,62 mm,
                          debaixo do corpo). A cor do corpo e o modelo.
  - membrane-keypad     : o teclado de membrana 4 x 4 de 69 x 76 mm, com o
                          rabicho e o conector femea de 8 vias (as linhas a
                          esquerda, como no de verdade).
  - analog-joystick     : o modulo KY-023 (a placa de 26 x 34 mm, a barra de
                          5 pinos a 90 graus: GND, +5V, VRx, VRy, SW).
  - ky-040              : o encoder KY-040 (a placa de 26 x 18,6 mm com o EC11
                          da ficha da Bourns PEC11R: 12,5 x 13,4 mm, o eixo de
                          6 mm em D, e a barra CLK, DT, SW, +, GND).
  - rotary-dialer       : o disco de telefone de 70 mm, com o batente e a
                          barra de bornes GND, DIAL, PULSE.
  - tilt-switch         : o modulo KY-020 (a chave de bolinha de metal, o
                          resistor de 10k e a barra -, +, S).
  - micro-switch        : a chave fim de curso KW11-3Z com roldana (a ficha da
                          KW4A da Dongnan: corpo de 19,8 x 10,2 mm, os
                          terminais C, NO e NC a 8,8 e 7,3 mm um do outro, a
                          roldana de 4,8 mm).

As partes que se mexem saem em "-movel" (e o resto em "-corpo"); o que o
editor precisa saber delas -- a capa que se aperta, o eixo do giro, o curso, as
chaves do DIP, as teclas e os furos do disco -- sai em px da caixa para
app/DesenhoDasEntradas.cs.
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
_spec_k = importlib.util.spec_from_file_location('keyes_a25', os.path.join(AQUI, 'keyes_a25.py'))
KEYES = importlib.util.module_from_spec(_spec_k)
_spec_k.loader.exec_module(KEYES)
mover, rodar = pa.mover, pa.rodar
ESTANHO = pa.ESTANHO
A = 96 / 25.4
PASSO = 2.54
BRANCO = '#f2f4f6'
PCB_PRETA, PCB_PRETA_BORDA = '#1b1d20', '#0a0b0c'
PLASTICO = '#1e1f22'

DEFS_EXTRA = '''  <radialGradient id="cupula" cx="0.36" cy="0.3" r="0.78"><stop offset="0" stop-color="#ffffff" stop-opacity="0.6"/><stop offset="0.32" stop-color="#ffffff" stop-opacity="0.1"/><stop offset="1" stop-color="#000000" stop-opacity="0.32"/></radialGradient>
  <radialGradient id="borda-capa" cx="0.5" cy="0.5" r="0.5"><stop offset="0.72" stop-color="#000000" stop-opacity="0"/><stop offset="1" stop-color="#000000" stop-opacity="0.45"/></radialGradient>
  <linearGradient id="aco" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#eef1f3"/><stop offset="0.5" stop-color="#c3c9cf"/><stop offset="1" stop-color="#959ca4"/></linearGradient>
  <radialGradient id="knob" cx="0.4" cy="0.35" r="0.7"><stop offset="0" stop-color="#4a4d52"/><stop offset="0.7" stop-color="#202124"/><stop offset="1" stop-color="#0d0e10"/></radialGradient>
  <radialGradient id="cromo" cx="0.38" cy="0.32" r="0.8"><stop offset="0" stop-color="#fbfcfd"/><stop offset="0.45" stop-color="#c9ced4"/><stop offset="0.8" stop-color="#8e959d"/><stop offset="1" stop-color="#b7bdc3"/></radialGradient>
'''

pecas = []
cs = []          # as linhas do DesenhoDasEntradas.cs


def svg_de(partes, L, H):
    w, h = L * A, H * A
    return (f'<svg xmlns="http://www.w3.org/2000/svg" version="1.1" width="{f(w)}px" height="{f(h)}px" '
            f'viewBox="0 0 {f(w)} {f(h)}">\n<defs>\n{pa.DEFS}{DEFS_EXTRA}</defs>\n' + ''.join(partes) + '</svg>\n')


def gravar(nome, partes, L, H):
    io.open(pa.BASE + f'app/assets/{nome}.svg', 'w', encoding='utf-8', newline='').write(svg_de(partes, L, H))


def gravar_movel(nome, corpo, movel, L, H):
    """O desenho inteiro, o corpo sem a parte que se mexe, e so ela."""
    gravar(nome, corpo.partes + movel.partes, L, H)
    gravar(nome + '-corpo', corpo.partes, L, H)
    gravar(nome + '-movel', movel.partes, L, H)


def guardar(id_peca, L, H, pinos, variantes=()):
    assert abs(L * A - round(L * A)) < 1e-6 and abs(H * A - round(H * A)) < 1e-6, (id_peca, L * A, H * A)
    pecas.append({'id': id_peca, 'largura': round(L * A), 'altura': round(H * A),
                  'pinos': [{'nome': n, 'x': round(x * A, 2), 'y': round(y * A, 2), 'sinais': s, 'descricao': dsc}
                            for n, x, y, s, dsc in pinos],
                  'variantes': list(variantes), 'metades': False})
    print(f'{id_peca}: {round(L * A)}x{round(H * A)} px, {len(pinos)} pinos')


def caixa(lmm, hmm):
    """A caixa em px inteiros, de volta em mm (o enquadramento do SVG pede)."""
    return round(lmm * A) / A, round(hmm * A) / A


def misturar(cor, outra, t):
    a = [int(cor[i:i + 2], 16) for i in (1, 3, 5)]
    b = [int(outra[i:i + 2], 16) for i in (1, 3, 5)]
    return '#' + ''.join(f'{round(x + (y - x) * t):02x}' for x, y in zip(a, b))


def px(v):
    return f'{v * A:.2f}'


def furo_m3(d, x, y, r=1.6, fundo='#0c0d0f'):
    d.circ(x, y, r + 0.65, ESTANHO)
    d.circ(x, y, r, fundo)


def pino_soldado(d, x, y, quadrado=False):
    if quadrado:
        d.ret_c(x, y, 1.6, 1.6, ESTANHO)
    else:
        d.circ(x, y, 0.8, ESTANHO)
    d.circ(x, y, 0.5, '#e3e6e9')
    d.circ(x - 0.14, y - 0.14, 0.18, '#ffffff', op=0.7)


def smd(d, cx, cy, comp, larg, corpo, vertical=False):
    """Um resistor/capacitor SMD: os dois terminais de estanho e o corpo."""
    if vertical:
        d.ret_c(cx, cy, larg, comp, ESTANHO, rx=0.08)
        d.ret_c(cx, cy, larg, comp * 0.56, corpo)
    else:
        d.ret_c(cx, cy, comp, larg, ESTANHO, rx=0.08)
        d.ret_c(cx, cy, comp * 0.56, larg, corpo)


def barra_90(d, x_borda, ys, comp, para='direita'):
    """A barra de pinos a 90 graus na borda da placa: o plastico em cima da
    placa e os pinos saindo para fora (comp mm alem da borda)."""
    if para == 'direita':
        d.ret(x_borda - 2.5, min(ys) - 1.27, 2.5, max(ys) - min(ys) + 2.54, PLASTICO, rx=0.15, stroke='#0b0c0d', sw=0.1)
        for y in ys:
            d.ret(x_borda - 1.0, y - 0.32, comp + 1.0, 0.64, 'url(#metal)', stroke='#80868d', sw=0.05)
            d.ret(x_borda + comp - 0.5, y - 0.32, 0.5, 0.64, '#e9ecef')
            d.linha(x_borda - 2.2, y, x_borda - 0.3, y, '#2c2e32', 0.25)
    else:   # para baixo
        d.ret(min(ys) - 1.27, x_borda - 2.5, max(ys) - min(ys) + 2.54, 2.5, PLASTICO, rx=0.15, stroke='#0b0c0d', sw=0.1)
        for x in ys:
            d.ret(x - 0.32, x_borda - 1.0, 0.64, comp + 1.0, 'url(#metal-h)', stroke='#80868d', sw=0.05)
            d.ret(x - 0.32, x_borda + comp - 0.5, 0.64, 0.5, '#e9ecef')
            d.linha(x, x_borda - 2.2, x, x_borda - 0.3, '#2c2e32', 0.25)


def cs_tupla(nome, x, y, r):
    cs.append(f'    public static readonly (double X, double Y, double R) {nome} = ({x * A:.2f}, {y * A:.2f}, {r * A:.2f});')


def cs_const(nome, v, comentario=None):
    if comentario:
        cs.append(f'    /// <summary>{comentario}</summary>')
    cs.append(f'    public const double {nome} = {v:.4f};')


# =====================================================================
#  pushbutton: a chave tatil de 12 x 12 com a capa redonda
# =====================================================================
CORES_DA_CAPA = {   # modelo: (cor, a do Wokwi)
    'vermelho': ('#d3272b', 'red'), 'verde': ('#2c9a48', 'green'), 'azul': ('#2560c8', 'blue'),
    'amarelo': ('#f2c21b', 'yellow'), 'branco': ('#eeeeea', 'white'), 'preto': ('#2a2b2e', 'black'),
    'cinza': ('#8f9399', 'gray'),
}
L, H = caixa(14.0, 12.7)
CX, CY = L / 2, H / 2
XS_BT = [CX - 6.35, CX + 6.35]                 # 12,7 mm: cinco furos da protoboard
YS_BT = [CY - PASSO, CY + PASSO]               # 5,08 mm
R_CAPA = 5.3


def tatil_12(d):
    for x in XS_BT:
        for y in YS_BT:
            x0 = 0.12 if x < CX else CX + 5.8
            d.ret(x0, y - 0.55, (CX - 5.8) - 0.12, 1.1, 'url(#metal-h)', rx=0.15, stroke='#6c727a', sw=0.08)
    d.ret_c(CX, CY, 12.0, 12.0, PLASTICO, rx=0.5, stroke='#0b0c0e', sw=0.15)
    d.ret_c(CX, CY, 11.3, 11.3, 'url(#aco)', rx=0.3, stroke='#7f868e', sw=0.1)
    for s in (-1, 1):                            # as abas dobradas da tampa
        d.ret_c(CX, CY + s * 5.5, 3.4, 0.5, '#868d95')
    for sx in (-1, 1):
        for sy in (-1, 1):                       # os quatro pinos de plastico da tampa
            d.circ(CX + sx * 4.65, CY + sy * 4.65, 0.78, '#151618', stroke='#5d636b', sw=0.1)
            d.circ(CX + sx * 4.65 - 0.2, CY + sy * 4.65 - 0.2, 0.22, '#ffffff', op=0.25)
    d.ret_c(CX, CY, 6.6, 6.6, '#1a1b1e', rx=0.3)    # o atuador quadrado, debaixo da capa


def capa_redonda(d, cor, apertada):
    escura, clara = misturar(cor, '#000000', 0.35), misturar(cor, '#ffffff', 0.25)
    dx, dy = (0.06, 0.1) if apertada else (0, 0)
    d.circ(CX + 0.3 - dx, CY + 0.4 - dy, R_CAPA + 0.05, '#000000', op=0.18 if apertada else 0.35)
    d.circ(CX + dx, CY + dy, R_CAPA, escura, stroke=misturar(cor, '#000000', 0.55), sw=0.12)
    d.circ(CX + dx, CY + dy, R_CAPA, 'url(#borda-capa)')
    topo = misturar(cor, '#000000', 0.12) if apertada else cor
    r_topo = 4.75 if apertada else 4.5
    d.circ(CX + dx - 0.12, CY + dy - 0.18, r_topo, topo)
    d.circ(CX + dx - 0.12, CY + dy - 0.18, r_topo, 'url(#cupula)', op=0.55 if apertada else 1)
    d.circ(CX + dx - 0.12, CY + dy - 0.18, r_topo * 0.72, None, stroke=clara, sw=0.12)


for modelo, (cor, _) in CORES_DA_CAPA.items():
    for estado in ('solto', 'pressionado'):
        d = Desenho(T(A, 0, 0, A, 0, 0))
        tatil_12(d)
        capa_redonda(d, cor, estado == 'pressionado')
        nome = 'pushbutton' + ('' if modelo == 'vermelho' else f'-{modelo}') + f'-{estado}'
        gravar(nome, d.partes, L, H)
        if modelo == 'vermelho' and estado == 'solto':
            gravar('pushbutton', d.partes, L, H)
DESC_BT = {'1.l': 'Perna 1 (esquerda): a mesma lamina do 1.r. Apertado, liga ao 2',
           '1.r': 'Perna 1 (direita): a mesma lamina do 1.l. Apertado, liga ao 2',
           '2.l': 'Perna 2 (esquerda): a mesma lamina do 2.r. Apertado, liga ao 1',
           '2.r': 'Perna 2 (direita): a mesma lamina do 2.l. Apertado, liga ao 1'}
guardar('pushbutton', L, H, [(n, x, y, [], DESC_BT[n]) for n, x, y in
                             (('1.l', XS_BT[0], YS_BT[0]), ('2.l', XS_BT[0], YS_BT[1]),
                              ('1.r', XS_BT[1], YS_BT[0]), ('2.r', XS_BT[1], YS_BT[1]))],
        ['pushbutton-solto', 'pushbutton-pressionado'])
cs_tupla('CapaPushbutton', CX, CY, 4.7)                 # o topo da capa: a borda fica para arrastar

# =====================================================================
#  pushbutton-6mm: a chave tatil de 6 x 6
# =====================================================================
L, H = caixa(8.75, 6.6)
CX, CY = L / 2, H / 2
XS_6 = [CX - 3 * PASSO / 2, CX + 3 * PASSO / 2]   # 7,62 mm
YS_6 = [CY - PASSO, CY + PASSO]


def tatil_6(d, apertada):
    for x in XS_6:
        for y in YS_6:
            x0 = 0.1 if x < CX else CX + 2.8
            d.ret(x0, y - 0.4, (CX - 2.8) - 0.1, 0.8, 'url(#metal-h)', rx=0.1, stroke='#6c727a', sw=0.06)
    d.ret_c(CX, CY, 6.0, 6.0, PLASTICO, rx=0.3, stroke='#0b0c0e', sw=0.12)
    d.ret_c(CX, CY, 5.7, 5.7, 'url(#aco)', rx=0.2, stroke='#7f868e', sw=0.08)
    for s in (-1, 1):
        d.ret_c(CX + s * 2.72, CY, 0.4, 2.2, '#868d95')     # as orelhas dobradas da tampa
        d.circ(CX + s * 1.95, CY - s * 1.95, 0.32, '#5b6169')  # os rebites
        d.circ(CX - s * 1.95, CY - s * 1.95, 0.32, '#5b6169')
    d.circ(CX, CY, 2.05, '#3b3f45')
    d.circ(CX + 0.12, CY + 0.15, 1.78, '#000000', op=0.3)
    topo = '#101113' if apertada else '#1c1d20'
    d.circ(CX, CY, 1.75, topo, stroke='#050506', sw=0.1)
    d.circ(CX - 0.35, CY - 0.4, 0.75 if not apertada else 0.55, '#ffffff', op=0.16 if not apertada else 0.08)


for estado in ('solto', 'pressionado'):
    d = Desenho(T(A, 0, 0, A, 0, 0))
    tatil_6(d, estado == 'pressionado')
    gravar(f'pushbutton-6mm-{estado}', d.partes, L, H)
    if estado == 'solto':
        gravar('pushbutton-6mm', d.partes, L, H)
guardar('pushbutton-6mm', L, H, [(n, x, y, [], DESC_BT[n]) for n, x, y in
                                 (('1.l', XS_6[0], YS_6[0]), ('2.l', XS_6[0], YS_6[1]),
                                  ('1.r', XS_6[1], YS_6[0]), ('2.r', XS_6[1], YS_6[1]))],
        ['pushbutton-6mm-solto', 'pushbutton-6mm-pressionado'])
cs_tupla('CapaPushbutton6mm', CX, CY, 2.6)

# =====================================================================
#  potentiometer: o WH148 de 16 mm, de frente, com o knob
# =====================================================================
L, H = caixa(17.0, 22.5)
CX, CY = L / 2, 8.7
XS_POT = [CX - 2 * PASSO, CX, CX + 2 * PASSO]      # o passo de 5 mm: dois furos
Y_POT = H - 0.95
corpo = Desenho(T(A, 0, 0, A, 0, 0))
for x in XS_POT:                                    # as tres pernas (lamina larga e ponta fina)
    corpo.ret(x - 0.9, CY + 5.5, 1.8, 6.3, 'url(#metal-h)', stroke='#70767e', sw=0.08)
    corpo.ret(x - 0.45, CY + 11.6, 0.9, H - 0.15 - (CY + 11.6), 'url(#metal-h)', stroke='#70767e', sw=0.06)
    corpo.circ(x, CY + 10.2, 0.45, '#5f656c')
corpo.ret_c(CX, CY + 7.4, 12.6, 1.6, '#4a3424', rx=0.3)   # a base de fenolite, atras da lata
corpo.circ(CX, CY, 8.0, 'url(#lata)', stroke='#757c84', sw=0.15)
corpo.circ(CX, CY, 7.35, None, stroke='#a3aab1', sw=0.25)
corpo.circ(CX, CY, 4.6, '#b9bfc5', stroke='#7c838b', sw=0.12)  # a bucha de 7 mm com a porca
corpo.ret_c(CX, CY - 6.9, 1.4, 0.9, '#9aa1a8', stroke='#6c737b', sw=0.08)   # a trava
corpo.texto(CX, CY + 7.0, 'B10K', 0.75, '#4f565d', alinhar='center')
corpo.circ(CX + 0.35, CY + 0.45, 6.55, '#000000', op=0.35)    # a sombra do knob
R_KNOB = 6.4
movel = Desenho(T(A, 0, 0, A, 0, 0))
dentes = []
for k in range(36):                                   # o serrilhado do knob
    a0 = 2 * math.pi * k / 36
    for da, r in ((0, R_KNOB), (0.45 * 2 * math.pi / 36, R_KNOB), (0.55 * 2 * math.pi / 36, R_KNOB - 0.35),
                  (2 * math.pi / 36, R_KNOB - 0.35)):
        dentes.append((CX + r * math.sin(a0 + da), CY - r * math.cos(a0 + da)))
movel.poli(dentes, '#1a1b1e', stroke='#0a0a0b', sw=0.08)
movel.circ(CX, CY, R_KNOB - 0.8, 'url(#knob)', stroke='#3c3f44', sw=0.12)
movel.circ(CX, CY, R_KNOB - 2.2, None, stroke='#2b2d31', sw=0.15)
movel.linha(CX, CY - R_KNOB + 1.0, CX, CY - 2.0, '#f4f4f0', 0.55)        # o risco: no meio do curso, para cima
gravar_movel('potentiometer', corpo, movel, L, H)
DESC_POT = {'GND': 'Perna 1 (esquerda): uma ponta da trilha. Em geral, o GND',
            'SIG': 'Perna 2 (do meio): o cursor. Vai ao pino analogico',
            'VCC': 'Perna 3 (direita): a outra ponta da trilha. Em geral, o 3,3 V ou o 5 V'}
guardar('potentiometer', L, H, [(n, x, Y_POT, s, DESC_POT[n]) for n, x, s in
                                zip(('GND', 'SIG', 'VCC'), XS_POT, (['power'], ['analog'], ['power']))])
cs_const('PotEixoX', CX / L, 'O eixo do knob do potenciometro, em fracao da caixa.')
cs_const('PotEixoY', CY / H)

# =====================================================================
#  slide-potentiometer: o Bourns PTA3043 (30 mm de curso) com o knob
# =====================================================================
L, H = caixa(45.6, 15.0)
X0, YC = (L - 45.0) / 2, H / 2
XC = X0 + 22.5
CURSO = 30.0
P1 = (X0 + 0.75, YC - 1.75)          # pino 1: uma ponta da trilha
P2 = (X0 + 0.75, YC + 1.75)          # pino 2: o cursor
P3 = (X0 + 0.75 + 43.5, YC - 1.75)   # pino 3: a outra ponta
corpo = Desenho(T(A, 0, 0, A, 0, 0))
corpo.ret(X0, YC - 4.5, 45.0, 9.0, 'url(#metal)', rx=0.35, stroke='#6b7279', sw=0.15)
corpo.ret(X0 + 4.0, YC - 3.6, 37.0, 7.2, '#c8cdd2', rx=0.3, stroke='#9aa1a8', sw=0.1)   # a tampa estampada
corpo.ret(X0 + 4.0, YC - 3.6, 37.0, 7.2, 'url(#luz)', rx=0.3)
corpo.ret_c(XC, YC, CURSO + 4.0, 1.3, '#16171a', rx=0.6)                                  # o rasgo
for x in (X0 + 2.0, X0 + 43.0):                                                           # os furos M2
    corpo.circ(x, YC, 1.05, '#2a2d31', stroke='#858c94', sw=0.15)
for (x, y) in (P1, P2, P3):                                                               # as pontas dos terminais
    corpo.circ(x, y, 0.42, '#e1e4e8', stroke='#6f767e', sw=0.08)
corpo.texto(X0 + 37.5, YC + 2.6, 'B10K', 0.8, '#5c636a', alinhar='center')
for k in range(7):                                                                        # a escala
    x = XC - CURSO / 2 + k * CURSO / 6
    corpo.linha(x, YC - 1.5, x, YC - 2.4 if k % 3 == 0 else YC - 2.0, '#6b7279', 0.18)
movel = Desenho(T(A, 0, 0, A, 0, 0))
XK = XC - CURSO / 2                   # o knob no comeco do curso (o editor anda com ele)
movel.ret_c(XK + 0.35, YC + 0.5, 8.0, 14.0, '#000000', rx=1.0, op=0.35)
movel.ret_c(XK, YC, 8.0, 14.0, 'url(#plastico)', rx=1.0, stroke='#0b0c0d', sw=0.12)
movel.ret_c(XK, YC, 6.6, 12.6, '#26282c', rx=0.7)
for k in range(-4, 5):
    if k:
        movel.linha(XK + k * 0.7, YC - 5.6, XK + k * 0.7, YC + 5.6, '#18191b', 0.22)
movel.linha(XK, YC - 6.0, XK, YC + 6.0, '#f2f2ee', 0.45)
gravar_movel('slide-potentiometer', corpo, movel, L, H)
guardar('slide-potentiometer', L, H, [
    ('GND', P1[0], P1[1], ['power'], 'Pino 1: a ponta da trilha do lado do knob no zero. Em geral, o GND'),
    ('SIG', P2[0], P2[1], ['analog'], 'Pino 2: o cursor. Vai ao pino analogico'),
    ('VCC', P3[0], P3[1], ['power'], 'Pino 3: a outra ponta da trilha. Em geral, o 3,3 V ou o 5 V')])
cs_const('DeslizanteCurso', CURSO * A, 'O curso do knob do deslizante (30 mm), em px.')

# =====================================================================
#  slide-switch: a SS12D00G4 de frente
# =====================================================================
L, H = caixa(9.2, 11.4)
CX = L / 2
XS_SS = [CX - PASSO, CX, CX + PASSO]
Y_SS = H - 0.6
CURSO_SS = 1.9
corpo = Desenho(T(A, 0, 0, A, 0, 0))
for x in XS_SS:
    corpo.ret(x - 0.28, 7.9, 0.56, H - 0.15 - 7.9, 'url(#metal-h)', stroke='#70767e', sw=0.05)
corpo.ret(CX - 4.3, 7.0, 8.6, 1.0, PLASTICO, rx=0.15)                        # a base de plastico
corpo.ret(CX - 4.3, 4.15, 8.6, 3.0, 'url(#metal)', rx=0.2, stroke='#6d747c', sw=0.1)   # a capa de metal
corpo.ret(CX - 1.95 - 0.1, 4.15, 3.9 + 0.2, 0.35, '#2a2c30')                 # o rasgo da alavanca
for s in (-1, 1):
    corpo.ret_c(CX + s * 3.6, 6.95, 0.9, 0.7, '#9aa1a8', stroke='#6d747c', sw=0.06)   # as abas dobradas
corpo.linha(CX - 4.1, 5.6, CX + 4.1, 5.6, '#8b9299', 0.12)
movel = Desenho(T(A, 0, 0, A, 0, 0))
XA = CX - CURSO_SS / 2                # a alavanca a esquerda: o comum (2) ligado ao 1
movel.ret(XA - 0.72, 0.25, 1.44, 4.1, 'url(#plastico)', rx=0.18, stroke='#0b0c0d', sw=0.08)
for y in (1.0, 1.7, 2.4):
    movel.linha(XA - 0.5, y, XA + 0.5, y, '#3a3c41', 0.14)
gravar_movel('slide-switch', corpo, movel, L, H)
guardar('slide-switch', L, H, [
    ('1', XS_SS[0], Y_SS, [], 'Um lado: ligado ao 2 com a alavanca a esquerda'),
    ('2', XS_SS[1], Y_SS, [], 'O comum: vai ao 1 (alavanca a esquerda) ou ao 3 (a direita)'),
    ('3', XS_SS[2], Y_SS, [], 'O outro lado: ligado ao 2 com a alavanca a direita')])
cs_tupla('CapaChaveDeslizante', CX, 2.6, 2.4)
cs_const('ChaveDeslizanteCurso', CURSO_SS * A, 'O curso da alavanca da chave deslizante (1,9 mm), em px.')

# =====================================================================
#  dip-switch-8: a DIP de 8 vias (0,3")
# =====================================================================
CORES_DO_DIP = {'vermelho': ('#c62129', '#7a1016'), 'azul': ('#1f57b9', '#0f2e6e'), 'preto': ('#1f2023', '#060607')}
L, H = caixa(22.0, 10.3)
CX, CY = L / 2, H / 2
XS_DIP = [CX + (k - 3.5) * PASSO for k in range(8)]
Y_A, Y_B = CY + 3 * PASSO / 2, CY - 3 * PASSO / 2      # 'a' embaixo, 'b' em cima: 7,62 mm
RASGO_L, RASGO_TOPO, RASGO_A = 1.5, CY - 2.45, 4.7
BOTAO_A, FOLGA = 2.1, 0.2
COR_RASGO = '#2b2527'
for modelo, (cor, borda) in CORES_DO_DIP.items():
    d = Desenho(T(A, 0, 0, A, 0, 0))
    d.ret_c(CX, CY, 21.6, 9.9, cor, rx=0.35, stroke=borda, sw=0.15)
    d.ret_c(CX, CY, 21.6, 9.9, 'url(#luz)', rx=0.35)
    for k, x in enumerate(XS_DIP):
        d.ret(x - RASGO_L / 2, RASGO_TOPO, RASGO_L, RASGO_A, COR_RASGO, rx=0.12)
        d.ret(x - RASGO_L / 2 + 0.15, RASGO_TOPO + RASGO_A - FOLGA - BOTAO_A, RASGO_L - 0.3, BOTAO_A, '#f6f6f1', rx=0.15)
        d.texto(x, CY + 3.55, str(k + 1), 1.05, BRANCO, alinhar='center')
    d.texto(XS_DIP[0] - 1.5, CY - 3.55, 'ON', 1.05, BRANCO, alinhar='center-left')
    xs = XS_DIP[0] + PASSO / 2                                    # a seta, entre a chave 1 e a 2
    d.poli([(xs, CY - 3.55 - 0.65), (xs - 0.5, CY - 3.55 + 0.2), (xs + 0.5, CY - 3.55 + 0.2)], BRANCO)
    d.linha(xs, CY - 3.55, xs, CY - 3.55 + 0.9, BRANCO, 0.28)
    gravar('dip-switch-8' + ('' if modelo == 'vermelho' else f'-{modelo}'), d.partes, L, H)
pinos_dip = [(f'{k + 1}a', x, Y_A, [], f'Chave {k + 1}, lado de baixo: ligado ao {k + 1}b com a chave em ON')
             for k, x in enumerate(XS_DIP)]
pinos_dip += [(f'{k + 1}b', x, Y_B, [], f'Chave {k + 1}, lado de cima: ligado ao {k + 1}a com a chave em ON')
              for k, x in reversed(list(enumerate(XS_DIP)))]
guardar('dip-switch-8', L, H, pinos_dip)
cs_const('DipPrimeira', (XS_DIP[0] - RASGO_L / 2) * A, 'As chaves do DIP: a borda do primeiro rasgo, o passo, o rasgo e a alavanca (px).')
cs_const('DipPasso', PASSO * A)
cs_const('DipLargura', RASGO_L * A)
cs_const('DipTopo', RASGO_TOPO * A)
cs_const('DipAltura', RASGO_A * A)
cs_const('DipAlavanca', BOTAO_A * A)
cs_const('DipFolga', FOLGA * A)
cs.append(f'    public const string DipCorDoRasgo = "{COR_RASGO}";')

# =====================================================================
#  membrane-keypad: o teclado 4 x 4 de membrana
# =====================================================================
L, H = caixa(69.6, 91.0)
X0, Y0 = (L - 69.0) / 2, 0.3
CX = L / 2
TECLA, PX_T, PY_T = 12.8, 15.6, 16.3
MX = X0 + (69.0 - (3 * PX_T + TECLA)) / 2
MY = Y0 + (76.0 - (3 * PY_T + TECLA)) / 2
TECLAS = ['123A', '456B', '789C', '*0#D']
Y_CON = H - 0.3 - 2.75
XS_KP = [CX + (k - 3.5) * PASSO for k in range(8)]
NOMES_KP = ['R1', 'R2', 'R3', 'R4', 'C1', 'C2', 'C3', 'C4']
d = Desenho(T(A, 0, 0, A, 0, 0))
# o rabicho: a fita transparente com as oito trilhas de prata
d.ret(CX - 10.6, Y0 + 75.0, 21.2, Y_CON - (Y0 + 75.0), '#d8d2bf', stroke='#a59f8c', sw=0.1)
for x in XS_KP:
    d.linha(x, Y0 + 76.0, x, Y_CON - 2.0, '#8f9196', 0.7, cap='butt')
d.ret(CX - 10.6, Y_CON - 6.0, 4 * PASSO + 0.6, 2.2, '#26272a', op=0.85)      # a faixa escura das linhas
# o teclado
d.ret(X0, Y0, 69.0, 76.0, '#1b1c1f', rx=1.6, stroke='#060607', sw=0.2)
d.ret(X0 + 0.9, Y0 + 0.9, 69.0 - 1.8, 76.0 - 1.8, None, rx=1.1, stroke='#3a3c41', sw=0.18)
teclas_cs = []
for i, linha in enumerate(TECLAS):
    for j, t in enumerate(linha):
        x, y = MX + j * PX_T, MY + i * PY_T
        cor = '#2867c9' if t.isdigit() else '#d1312e'
        d.ret(x + 0.25, y + 0.35, TECLA, TECLA, '#000000', rx=1.4, op=0.35)
        d.ret(x, y, TECLA, TECLA, cor, rx=1.4, stroke=misturar(cor, '#000000', 0.4), sw=0.15)
        d.ret(x, y, TECLA, TECLA, 'url(#luz)', rx=1.4)
        d.ret(x + 0.9, y + 0.9, TECLA - 1.8, TECLA - 1.8, None, rx=1.0, stroke=misturar(cor, '#ffffff', 0.3), sw=0.12)
        d.texto(x + TECLA / 2, y + TECLA / 2 + 0.2, t, 3.6 if t != '*' else 4.6, '#ffffff', alinhar='center', peso='normal')
        teclas_cs.append((x + TECLA / 2, y + TECLA / 2, t))
# o conector femea de 8 vias
d.ret_c(CX, Y_CON, 8 * PASSO + 0.5, 5.5, PLASTICO, rx=0.25, stroke='#0a0a0b', sw=0.12)
for k, x in enumerate(XS_KP):
    d.ret_c(x, Y_CON, 1.05, 1.05, '#060607')
    d.ret_c(x, Y_CON, 0.55, 0.55, '#c9a85a')
gravar('membrane-keypad', d.partes, L, H)
DESC_KP = {**{f'R{k}': f'Linha {k} (as teclas {TECLAS[k - 1]})' for k in range(1, 5)},
           **{f'C{k}': f'Coluna {k} (as teclas {"".join(l[k - 1] for l in TECLAS)})' for k in range(1, 5)}}
guardar('membrane-keypad', L, H, [(n, x, Y_CON, [], DESC_KP[n]) for n, x in zip(NOMES_KP, XS_KP)])
cs.append('    /// <summary>O centro de cada tecla do teclado 4 x 4 (px) e meia tecla.</summary>')
cs.append('    public static readonly (double X, double Y, string Tecla)[] Teclas =\n    [\n'
          + ''.join(f'        ({x * A:.2f}, {y * A:.2f}, "{t}"),\n' for x, y, t in teclas_cs) + '    ];')
cs_const('MeiaTecla', TECLA / 2 * A)

# =====================================================================
#  analog-joystick: o KY-023
# =====================================================================
L, H = caixa(26.45, 40.5)
X0, Y0 = (L - 26.0) / 2, 0.2
CX, CJ = L / 2, Y0 + 14.0
R_CAPA_J = 10.0
NOMES_J = [('GND', 'GND', ['power'], 'GND'), ('VCC', '+5V', ['power'], 'Alimentacao (+5V na placa): 3,3 V ou 5 V'),
           ('HORZ', 'VRx', ['analog'], 'Eixo X (VRx na placa): a tensao do potenciometro horizontal'),
           ('VERT', 'VRy', ['analog'], 'Eixo Y (VRy na placa): a tensao do potenciometro vertical'),
           ('SEL', 'SW', [], 'O botao do manche (SW na placa): vai ao GND apertado (use INPUT_PULLUP)')]
XS_J = [CX + (k - 2) * PASSO for k in range(5)]
Y_J = H - 1.0
CURSO_J = 6.0
corpo = Desenho(T(A, 0, 0, A, 0, 0))
corpo.ret(X0, Y0, 26.0, 34.0, PCB_PRETA, rx=0.8, stroke=PCB_PRETA_BORDA, sw=0.15)
corpo.ret(X0, Y0, 26.0, 34.0, 'url(#luz)', rx=0.8)
for x in (X0 + 2.6, X0 + 23.4):
    for y in (Y0 + 2.6, Y0 + 31.4):
        furo_m3(corpo, x, y)
for k, x in enumerate(XS_J):
    pino_soldado(corpo, x, Y0 + 29.3, quadrado=k == 0)
    corpo.texto(x, Y0 + 27.0, NOMES_J[k][1], 0.75, BRANCO, ang=90, alinhar='center')
barra_90(corpo, Y0 + 34.0, XS_J, 6.0, para='baixo')
# o mecanismo: a base, os dois potenciometros e a chave do manche
corpo.ret_c(CX - 9.6, CJ, 3.2, 10.5, '#1f3f8f', rx=0.3, stroke='#10224d', sw=0.1)        # o potenciometro do X
corpo.ret_c(CX, CJ + 9.6, 10.5, 3.2, '#1f3f8f', rx=0.3, stroke='#10224d', sw=0.1)        # o do Y
for s in (-1, 1):
    corpo.ret_c(CX - 9.6, CJ + s * 3.4, 1.2, 1.4, '#c8cdd2')
    corpo.ret_c(CX + s * 3.4, CJ + 9.6, 1.4, 1.2, '#c8cdd2')
corpo.ret_c(CX + 9.4, CJ + 2.0, 2.8, 5.0, '#2a2b2e', rx=0.3)                           # a chave do SW
corpo.ret_c(CX, CJ, 16.4, 16.4, 'url(#aco)', rx=0.6, stroke='#6c737b', sw=0.15)         # o suporte de metal
corpo.ret_c(CX, CJ, 13.0, 13.0, '#222326', rx=1.5)
corpo.circ(CX, CJ, 6.2, '#141517', stroke='#3d4046', sw=0.2)
corpo.circ(CX + 0.4, CJ + 0.6, R_CAPA_J, '#000000', op=0.3)                             # a sombra da capa
movel = Desenho(T(A, 0, 0, A, 0, 0))
movel.circ(CX, CJ, R_CAPA_J, 'url(#knob)', stroke='#08090a', sw=0.2)
movel.circ(CX, CJ, R_CAPA_J - 1.1, '#26282c', stroke='#3d4046', sw=0.15)
for k in range(24):                                                                     # o anel antiderrapante
    a = 2 * math.pi * k / 24
    movel.circ(CX + 7.6 * math.cos(a), CJ + 7.6 * math.sin(a), 0.38, '#17181a')
movel.circ(CX, CJ, 5.6, '#1e1f22', stroke='#34373c', sw=0.15)
movel.circ(CX - 1.6, CJ - 2.0, 3.0, '#ffffff', op=0.06)
gravar_movel('analog-joystick', corpo, movel, L, H)
guardar('analog-joystick', L, H, [(n, XS_J[k], Y_J, s, dsc) for k, (n, _, s, dsc) in enumerate(NOMES_J)])
cs_tupla('CapaJoystick', CX, CJ, R_CAPA_J)
cs_const('JoystickEixoX', CX / L, 'O centro do manche do joystick (fracao da caixa) e o curso de ponta a ponta (6 mm, em px).')
cs_const('JoystickEixoY', CJ / H)
cs_const('JoystickCurso', CURSO_J * A)

# =====================================================================
#  ky-040: o encoder rotativo
# =====================================================================
# Pelo desenho com cota do modulo (26,2 x 18,63 mm): a barra 1,3 mm para dentro
# da borda, os pinos na parte de cima (o CLK a 2 mm do topo), os dois furos de
# 3,4 mm embaixo, o EC11 a esquerda com as tres pernas A-C-B saindo pela
# esquerda e as abas do quadro de metal em cima e embaixo, os tres 10k dos
# pull-ups e os rotulos grandes.
LP_KY, HP_KY = 26.2, 18.63
L, H = caixa(0.2 + LP_KY + 6.1, HP_KY + 0.4)
X0, Y0 = 0.2, 0.2
XE, CY = X0 + 9.5, Y0 + 7.65
NOMES_KY = [('CLK', 'CLK', [], 'O canal A do encoder (CLK): pull-up de 10k no modulo'),
            ('DT', 'DT', [], 'O canal B do encoder (DT): pull-up de 10k no modulo'),
            ('SW', 'SW', [], 'O botao do eixo: vai ao GND apertado (use INPUT_PULLUP)'),
            ('VCC', '+', ['power'], 'Alimentacao (+ na placa): 3,3 V ou 5 V, para os pull-ups'),
            ('GND', 'GND', ['power'], 'GND')]
YS_KY = [Y0 + 2.0 + k * PASSO for k in range(5)]
X_KY = L - 0.9
corpo = Desenho(T(A, 0, 0, A, 0, 0))
corpo.ret(X0 + 0.15, Y0 + 0.2, LP_KY, HP_KY, '#000000', rx=0.5, op=0.35)
corpo.ret(X0, Y0, LP_KY, HP_KY, KEYES.PRETA, rx=0.5, stroke=KEYES.PRETA_BORDA, sw=0.15)
KEYES.textura(corpo, X0 + 0.2, Y0 + 0.2, LP_KY - 0.4, HP_KY - 0.4)
corpo.ret(X0, Y0, LP_KY, HP_KY, 'url(#luz)', rx=0.5)
for x in (X0 + 4.5, X0 + 21.7):                                                     # os furos de 3,4 mm
    KEYES.ilha(corpo, x, Y0 + 16.8, 1.95, 1.7)
for k, y in enumerate(YS_KY):
    corpo.texto(X0 + 21.0, y, NOMES_KY[k][1], 1.35, BRANCO, alinhar='center-right', peso='bold')
corpo.ret(X0 + 21.85, YS_KY[0] - 1.6, 3.2, YS_KY[-1] - YS_KY[0] + 3.2, None, rx=0.15, stroke=BRANCO, sw=0.15)
E_ = KEYES.E
for k in range(3):                                                                   # os pull-ups de 10k
    E_.passivo(corpo, X0 + 11.4 + k * 2.0, Y0 + 16.4, '0603', 'R', '103', vertical=True)
barra_90(corpo, X0 + 24.9, YS_KY, L - 0.15 - (X0 + 24.9))
# o EC11: as pernas A-C-B pela esquerda, as abas do quadro, a base, a tampa de metal e a bucha
for y in (Y0 + 4.7, Y0 + 7.3, Y0 + 9.9):
    corpo.ret(X0 + 2.0, y - 0.35, 1.4, 0.7, 'url(#metal)', rx=0.1, stroke='#6c737b', sw=0.05)
    corpo.circ(X0 + 2.15, y, 0.55, ESTANHO)
for sy in (-1, 1):
    corpo.ret_c(XE, CY + sy * 6.0, 1.6, 1.4, 'url(#metal)', rx=0.15, stroke='#6c737b', sw=0.08)
E_.sombra(corpo, XE - 6.3, CY - 5.45, 12.6, 10.9, rx=0.8, op=0.4)
corpo.ret_c(XE, CY, 12.6, 10.9, '#2a8a6a', rx=0.8, stroke='#145040', sw=0.12)          # a base verde do EC11
corpo.ret_c(XE, CY, 11.6, 10.3, 'url(#aco)', rx=0.7, stroke='#727981', sw=0.1)        # o quadro de metal
corpo.ret_c(XE, CY, 11.6, 10.3, 'url(#luz)', rx=0.7)
for sx in (-1, 1):
    for sy in (-1, 1):
        corpo.circ(XE + sx * 4.6, CY + sy * 4.0, 0.45, '#7c838b')
corpo.circ(XE, CY, 3.5, '#d4d8dc', stroke='#7c838b', sw=0.12)
for r in (3.1, 2.75, 2.4):                                                            # a rosca M7
    corpo.circ(XE, CY, r + 0.6, None, stroke='#9aa1a8', sw=0.1)
movel = Desenho(T(A, 0, 0, A, 0, 0))
R_EIXO = 3.0
pts = []
for k in range(60):                                                                   # o eixo serrilhado, com o D
    a = 2 * math.pi * k / 60
    x, y = R_EIXO * math.sin(a), -R_EIXO * math.cos(a)
    if y < -1.5:                                                                      # o chanfro do D, em cima
        x, y = x * 1.5 / -y if abs(y) > 1e-9 else x, -1.5
    elif k % 2:
        x, y = x * 0.93, y * 0.93
    pts.append((XE + x, CY + y))
movel.poli(pts, 'url(#cromo)', stroke='#6b727a', sw=0.08)
movel.linha(XE - 2.4, CY - 1.5, XE + 2.4, CY - 1.5, '#6b727a', 0.12)
movel.linha(XE, CY - 1.2, XE, CY + 2.5, '#5d646b', 0.35)                               # o rasgo do eixo
gravar_movel('ky-040', corpo, movel, L, H)
guardar('ky-040', L, H, [(n, X_KY, YS_KY[k], s, dsc) for k, (n, _, s, dsc) in enumerate(NOMES_KY)])
cs_tupla('CapaEncoder', XE, CY, 3.6)
cs_const('EncoderEixoX', XE / L, 'O eixo do encoder, em fracao da caixa.')
cs_const('EncoderEixoY', CY / H)

# =====================================================================
#  rotary-dialer: o disco de telefone
# =====================================================================
L, H = caixa(70.4, 77.5)
CX, CY = L / 2, 35.4
R_DISCO, R_FUROS, R_FURO = 32.3, 25.5, 5.6
ANG = {n: -5 - 30 * ((10 if n == 0 else n) - 1) for n in range(10)}   # graus na tela (horario)
BATENTE = 25.0
XS_DD = [CX - 2 * PASSO, CX, CX + 2 * PASSO]
Y_DD = H - 3.2


def no_circulo(r, ang):
    a = math.radians(ang)
    return CX + r * math.cos(a), CY + r * math.sin(a)


corpo = Desenho(T(A, 0, 0, A, 0, 0))
# a barra de bornes
corpo.ret_c(CX, H - 3.6, 17.0, 6.4, PLASTICO, rx=0.6, stroke='#0a0a0b', sw=0.12)
for x, n in zip(XS_DD, ('GND', 'DIAL', 'PULSE')):
    corpo.circ(x, Y_DD, 1.55, 'url(#lata)', stroke='#5f666e', sw=0.1)
    corpo.linha(x - 1.0, Y_DD - 1.0, x + 1.0, Y_DD + 1.0, '#5f666e', 0.3)
    corpo.texto(x, H - 5.75, n, 0.8, BRANCO, alinhar='center')
# o aro, o cartao dos numeros e o batente
corpo.circ(CX, CY, 35.0, '#141517', stroke='#050506', sw=0.2)
corpo.circ(CX, CY, 34.2, None, stroke='#9aa1a8', sw=0.35)
corpo.circ(CX, CY, R_DISCO + 0.6, '#f3f1ea')
for n, a in ANG.items():
    x, y = no_circulo(R_FUROS, a)
    corpo.texto(x, y + 0.15, str(n), 4.0, '#161616', alinhar='center')
corpo.circ(CX, CY, 12.6, '#f6f4ee', stroke='#9b1d1d', sw=0.45)
for k, y in enumerate((-4.2, -1.4, 1.4, 4.2)):
    corpo.linha(CX - 7.5 + abs(k - 1.5) * 1.6, CY + y, CX + 7.5 - abs(k - 1.5) * 1.6, CY + y, '#b9b4a6', 0.18)
bx, by = no_circulo(34.0, BATENTE)
corpo.entrar(pa.mover(bx, by) * pa.rodar(BATENTE))
corpo.ret(-1.8, -1.3, 3.6, 2.6, 'url(#metal)', rx=1.1, stroke='#3f454d', sw=0.18)
corpo.ret(-1.6, -0.35, 1.2, 0.7, '#5d646b', rx=0.3)
corpo.circ(0.9, 0, 0.55, '#6d747c', stroke='#3f454d', sw=0.1)
corpo.sair()
movel = Desenho(T(A, 0, 0, A, 0, 0))
# a roda dos dedos: o anel com os dez furos (vazados: os numeros aparecem por eles)


def circulo_svg(cx, cy, r, horario):
    X, Y, R = cx * A, cy * A, r * A
    s = 1 if horario else 0
    return f'M{X + R:.2f} {Y:.2f} A{R:.2f} {R:.2f} 0 1 {s} {X - R:.2f} {Y:.2f} A{R:.2f} {R:.2f} 0 1 {s} {X + R:.2f} {Y:.2f} Z '


roda = circulo_svg(CX, CY, R_DISCO, True) + circulo_svg(CX, CY, 13.0, False)
for n, a in ANG.items():
    roda += circulo_svg(*no_circulo(R_FUROS, a), R_FURO, False)
movel += f'<path d="{roda}" fill="url(#cromo)" fill-rule="evenodd" stroke="#59606a" stroke-width="{0.2 * A:.2f}"/>\n'
for n, a in ANG.items():
    x, y = no_circulo(R_FUROS, a)
    movel.circ(x, y, R_FURO + 0.1, None, stroke='#3f454d', sw=0.35)
movel.circ(CX, CY, 13.2, None, stroke='#5d646b', sw=0.35)
movel.circ(CX, CY, R_DISCO - 0.4, None, stroke='#ffffff', sw=0.3)
gravar_movel('rotary-dialer', corpo, movel, L, H)
guardar('rotary-dialer', L, H, [
    ('GND', XS_DD[0], Y_DD, ['power'], 'O comum dos dois contatos do disco'),
    ('DIAL', XS_DD[1], Y_DD, [], 'Vai ao GND enquanto o disco esta fora do lugar (use INPUT_PULLUP)'),
    ('PULSE', XS_DD[2], Y_DD, [], 'Um pulso por numero na volta do disco (o 0 da dez)')])
cs.append('    /// <summary>Os dez furos do disco (px): o "1" no batente, cada um 30 graus antes.</summary>')
cs.append('    public static readonly (double X, double Y, int Numero)[] Furos =\n    [\n'
          + ''.join(f'        ({no_circulo(R_FUROS, ANG[n])[0] * A:.2f}, {no_circulo(R_FUROS, ANG[n])[1] * A:.2f}, {n}),\n'
                    for n in (1, 2, 3, 4, 5, 6, 7, 8, 9, 0)) + '    ];')
cs_const('RaioDoFuro', R_FURO * A)
cs_const('DiscoEixoX', CX / L, 'O eixo do disco, em fracao da caixa.')
cs_const('DiscoEixoY', CY / H)

# =====================================================================
#  tilt-switch: o KY-020
# =====================================================================
L, H = caixa(24.6, 15.35)
X0, Y0 = 0.2, (H - 15.0) / 2
CY = H / 2
YS_T = [CY - PASSO, CY, CY + PASSO]
X_T = L - 0.9
d = Desenho(T(A, 0, 0, A, 0, 0))
# A placa universal dos KY (keyes_a25.py) e a chave de bolinha da foto de cima do
# KY-020: o tubo com a capa de epoxi azul deitado na diagonal, a tampa branca do
# lado das pernas, uma perna na oval de baixo e a outra na ilha de cima.
KEYES.placa(d, X0, Y0, YS_T, L - 0.15 - (X0 + 18.5), simbolo=False)
for (xa, ya), (xb, yb) in (((X0 + 5.4, Y0 + 9.4), (X0 + 5.4, Y0 + 10.0)), ((X0 + 10.9, Y0 + 5.0), (X0 + 11.9, Y0 + 5.1))):
    KEYES.perna(d, xa, ya, xb, yb)
d.entrar(mover(X0 + 8.1, Y0 + 7.3) * rodar(-33))
d.ret_c(0.3, 0.45, 8.6, 4.6, '#000000', rx=1.6, op=0.35)
d.ret_c(-3.6, 0, 1.8, 4.0, '#efeee8', rx=0.5, stroke='#b9b6a9', sw=0.1)        # a tampa branca
d.ret_c(-3.6, 0, 0.7, 1.8, '#c8a75a', rx=0.15)                                 # a perna dobrada
d.ret_c(0.9, 0, 7.2, 4.6, '#2f6fd0', rx=1.6, stroke='#1a4a92', sw=0.12)        # a capa de epoxi
d.ret_c(0.9, -1.2, 6.2, 1.0, '#ffffff', rx=0.5, op=0.22)
d.ret_c(0.9, 0, 7.2, 4.6, 'url(#luz)', rx=1.6)
d.sair()
gravar('tilt-switch', d.partes, L, H)
guardar('tilt-switch', L, H, [
    ('GND', X_T, YS_T[0], ['power'], 'GND (- na placa)'),
    ('VCC', X_T, YS_T[1], ['power'], 'Alimentacao (o pino do meio): 3,3 V ou 5 V, pelo resistor de 10k'),
    ('OUT', X_T, YS_T[2], [], 'A saida (S na placa): muda de nivel quando o modulo inclina')])

# =====================================================================
#  micro-switch: a KW11-3Z com roldana, de lado
# =====================================================================
L, H = caixa(20.1, 22.75)
X0 = (L - 19.8) / 2
YT = 8.7                                  # o topo do corpo
YB = YT + 10.2
XS_MS = {'COM': X0 + 1.85, 'NO': X0 + 10.65, 'NC': X0 + 17.95}
Y_MS = YB + 2.3
HX, HY = X0 + 1.8, YT - 0.15             # a dobradica da alavanca
RX, RY, RR = X0 + 14.5, 2.7, 2.4         # a roldana
XP = X0 + 7.7                            # o pino do botao
corpo = Desenho(T(A, 0, 0, A, 0, 0))
for n, x in XS_MS.items():
    corpo.ret(x - 1.3, YB - 0.4, 2.6, 3.9, 'url(#metal-h)', rx=0.2, stroke='#73797f', sw=0.08)
    corpo.circ(x, Y_MS, 0.6, '#16171a')
corpo.ret(X0, YT, 19.8, 10.2, PLASTICO, rx=0.4, stroke='#0a0a0b', sw=0.15)
corpo.ret(X0, YT, 19.8, 10.2, 'url(#luz)', rx=0.4)
corpo.ret(X0 + 0.5, YT + 0.5, 18.8, 9.2, None, rx=0.3, stroke='#2f3135', sw=0.12)
for x in (X0 + 5.15, X0 + 14.65):          # os furos de fixacao (9,5 mm)
    corpo.circ(x, YB - 3.6, 1.3, '#0b0c0d', stroke='#3a3c41', sw=0.15)
corpo.texto(X0 + 9.9, YT + 2.3, 'KW11-3Z', 0.9, '#62666c', alinhar='center')
corpo.texto(X0 + 9.9, YT + 3.9, '5A 250VAC', 0.75, '#55595f', alinhar='center')
for n, x in XS_MS.items():
    corpo.texto(x if n != 'COM' else x + 0.6, YB - 1.0, 'C' if n == 'COM' else n, 0.85, '#7a7e84', alinhar='center')
corpo.ret_c(XP, YT - 0.35, 2.0, 1.1, '#c7362f', rx=0.25, stroke='#7a1c18', sw=0.08)      # o pino do botao
corpo.ret(X0 + 0.5, YT - 0.7, 2.8, 1.3, 'url(#metal)', rx=0.3, stroke='#6c737b', sw=0.08)  # o suporte da dobradica
movel = Desenho(T(A, 0, 0, A, 0, 0))
BX, BY = X0 + 11.5, YT - 1.9               # a dobra da alavanca
movel.linha(HX, HY, BX, BY, '#6c737b', 0.62, cap='round')
movel.linha(HX, HY, BX, BY, '#d3d8dd', 0.42, cap='round')
movel.poli([(BX - 0.2, BY - 0.25), (RX - 0.55, RY + 0.4), (RX + 0.55, RY + 0.4), (BX + 0.6, BY + 0.2)],
           'url(#metal)', stroke='#6c737b', sw=0.08)
movel.circ(HX, HY, 0.55, '#7d838c', stroke='#4f555c', sw=0.08)
movel.circ(RX, RY, RR, '#ece9e1', stroke='#8f8c84', sw=0.18)
movel.circ(RX, RY, RR - 0.6, None, stroke='#cfcbc1', sw=0.15)
movel.circ(RX, RY, 0.6, '#7d838c')
gravar_movel('micro-switch', corpo, movel, L, H)
DESC_MS = {'COM': 'Comum (C na chave): o que a chave liga ao NC (solta) ou ao NO (apertada). Em geral, o GND',
           'NO': 'Normalmente aberto: ligado ao COM so com a alavanca apertada (num GPIO com INPUT_PULLUP, le LOW apertada)',
           'NC': 'Normalmente fechado: ligado ao COM com a alavanca solta'}
guardar('micro-switch', L, H, [(n, x, Y_MS, [], DESC_MS[n]) for n, x in XS_MS.items()])
# apertada, a alavanca desce ate o pino do botao entrar (0,8 mm de curso, mais a folga)
lever_y = HY + (XP - HX) * (BY - HY) / (BX - HX)
giro = math.degrees(math.atan2((YT - 0.1) - (lever_y + 0.21), XP - HX))
cs_tupla('CapaFimDeCurso', X0 + 10.6, YT - 2.6, 6.0)
cs_const('FimDeCursoEixoX', HX / L, 'A dobradica da alavanca do fim de curso (fracao da caixa) e quanto ela gira apertada (graus).')
cs_const('FimDeCursoEixoY', HY / H)
cs_const('FimDeCursoGiro', giro)

# =====================================================================
cam = pa.BASE + 'app/assets/pecas.json'
dados = json.load(io.open(cam, encoding='utf-8'))
novos = {p['id'] for p in pecas}
dados = [p for p in dados if p['id'] not in novos] + pecas
dados.sort(key=lambda p: p['id'])
io.open(cam, 'w', encoding='utf-8', newline='').write(json.dumps(dados, ensure_ascii=False, indent=1))
print(f'\ncatalogo: {len(dados)} pecas')

cs_texto = ('// Gerado por ferramentas/extrair-svg/pecas-entradas.py -- nao edite a mao.\n'
            'namespace Simulador.App;\n\n'
            '/// <summary>\n'
            '/// As medidas das entradas da Alpha 25 que o editor usa, em px da caixa de\n'
            '/// cada peca: a capa que se aperta, o eixo do que gira, o curso do que\n'
            '/// desliza, as chaves do DIP, as teclas do teclado e os furos do disco.\n'
            '/// Saem do mesmo gerador dos desenhos: o clique cai onde a pessoa ve.\n'
            '/// </summary>\n'
            'static class DesenhoDasEntradas\n{\n' + '\n'.join(cs) + '\n}\n')
io.open(pa.BASE + 'app/DesenhoDasEntradas.cs', 'w', encoding='utf-8', newline='').write(cs_texto)
print('app/DesenhoDasEntradas.cs')
