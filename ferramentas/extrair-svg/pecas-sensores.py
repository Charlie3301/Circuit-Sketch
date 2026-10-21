"""python ferramentas/extrair-svg/pecas-sensores.py

Os sensores da Alpha 25, no estilo das placas e no tamanho real (as medidas
dos fabricantes e dos vendedores; os pinos nos lugares de verdade):

  - hc-sr04               : o ultrassom de 45 x 20 mm, de frente: os dois
                            transdutores de 16 mm (T e R), o cristal no meio, a
                            barra VCC, Trig, Echo, Gnd embaixo;
  - dht22 / dht11         : os sensores soltos (AM2302 de 15,1 x 25 mm, branco;
                            DHT11 de 12 x 15,5 mm, azul), as quatro pernas no
                            passo de 2,54 mm -- o DHT11 ganhou o NC que tem;
  - photoresistor-sensor  : o modulo do LDR de 32 x 14 mm (LM393, trimpot, os
                            LEDs PWR e DO), a barra VCC, GND, DO, AO;
  - flame-sensor          : o mesmo modulo com o receptor infravermelho de 5 mm;
  - gas-sensor            : o modulo do MQ-2 de 32 x 22 mm (a tela de aco dupla);
  - ntc-temperature-sensor: o KY-013 (o NTC de gota e o resistor de 10k);
  - heart-beat-sensor     : o KY-039 (o LED infravermelho e o fototransistor);
  - big-sound-sensor      : o KY-037 (o microfone de 9,7 mm);
  - small-sound-sensor    : o KY-038 (o microfone de 6 mm);
  - pir-motion-sensor     : o HC-SR501 de 32 x 24 mm, a lente de Fresnel de 23 mm
                            e os furos M2 a 28 mm;
  - mpu6050               : o GY-521 de 21 x 16 mm, os oito furos na ordem da
                            placa (VCC, GND, SCL, SDA, XDA, XCL, AD0, INT);
  - hx711                 : o modulo verde de 38 x 21 mm, com os fios da celula
                            de carga soldados no E+, E-, A- e A+.

Os LEDs de energia e o do DO dos modulos (as posicoes saem impressas para o
EditorModulos.cs) acendem pela conta: o PWR com o VCC ligado, o DO quando o
sensor detecta.
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
ESTANHO = pa.ESTANHO
A = 96 / 25.4
PASSO = 2.54
BRANCO = '#f2f4f6'
PLASTICO = '#1e1f22'
AZUL, AZUL_BORDA = '#1d4fa6', '#0f2c66'
PRETA, PRETA_BORDA = '#1b1d20', '#0a0b0c'
VERMELHA, VERMELHA_BORDA = '#b8232b', '#6e1015'
VERDE, VERDE_BORDA = '#1f7a3c', '#0f4320'

DEFS_EXTRA = '''  <radialGradient id="cupula" cx="0.36" cy="0.3" r="0.78"><stop offset="0" stop-color="#ffffff" stop-opacity="0.6"/><stop offset="0.32" stop-color="#ffffff" stop-opacity="0.1"/><stop offset="1" stop-color="#000000" stop-opacity="0.32"/></radialGradient>
  <radialGradient id="lente" cx="0.42" cy="0.36" r="0.7"><stop offset="0" stop-color="#ffffff"/><stop offset="0.7" stop-color="#eceeec"/><stop offset="1" stop-color="#c9cdcb"/></radialGradient>
  <radialGradient id="tela-aco" cx="0.4" cy="0.35" r="0.75"><stop offset="0" stop-color="#e4e8eb"/><stop offset="0.75" stop-color="#a9b0b7"/><stop offset="1" stop-color="#7f868e"/></radialGradient>
'''

pecas = []
luzes = []          # (peca, x px, y px, raio px, cor, chave) para o EditorModulos.cs


def svg_de(d, L, H):
    w, h = L * A, H * A
    return (f'<svg xmlns="http://www.w3.org/2000/svg" version="1.1" width="{f(w)}px" height="{f(h)}px" '
            f'viewBox="0 0 {f(w)} {f(h)}">\n<defs>\n{pa.DEFS}{DEFS_EXTRA}</defs>\n' + ''.join(d.partes) + '</svg>\n')


def gravar(nome, d, L, H):
    io.open(pa.BASE + f'app/assets/{nome}.svg', 'w', encoding='utf-8', newline='').write(svg_de(d, L, H))


def guardar(id_peca, L, H, pinos):
    assert abs(L * A - round(L * A)) < 1e-6 and abs(H * A - round(H * A)) < 1e-6, (id_peca, L * A, H * A)
    pecas.append({'id': id_peca, 'largura': round(L * A), 'altura': round(H * A),
                  'pinos': [{'nome': n, 'x': round(x * A, 2), 'y': round(y * A, 2), 'sinais': s, 'descricao': dsc}
                            for n, x, y, s, dsc in pinos],
                  'variantes': [], 'metades': False})
    print(f'{id_peca}: {round(L * A)}x{round(H * A)} px, {len(pinos)} pinos')


def caixa(lmm, hmm):
    return round(lmm * A) / A, round(hmm * A) / A


def placa(d, x, y, w, h, cor, borda, rx=0.8):
    d.ret(x, y, w, h, cor, rx=rx, stroke=borda, sw=0.15)
    d.ret(x, y, w, h, 'url(#luz)', rx=rx)


def furo(d, x, y, r=1.5, fundo='#0c0d0f'):
    d.circ(x, y, r + 0.6, ESTANHO)
    d.circ(x, y, r, fundo)


def ilha(d, x, y, quadrada=False):
    if quadrada:
        d.ret_c(x, y, 1.6, 1.6, ESTANHO)
    else:
        d.circ(x, y, 0.8, ESTANHO)
    d.circ(x, y, 0.5, '#e3e6e9')
    d.circ(x - 0.14, y - 0.14, 0.18, '#ffffff', op=0.7)


def smd(d, cx, cy, comp, larg, corpo, vertical=False):
    if vertical:
        d.ret_c(cx, cy, larg, comp, ESTANHO, rx=0.08)
        d.ret_c(cx, cy, larg, comp * 0.56, corpo)
    else:
        d.ret_c(cx, cy, comp, larg, ESTANHO, rx=0.08)
        d.ret_c(cx, cy, comp * 0.56, larg, corpo)


def led_smd(d, cx, cy, vertical=False):
    """O LED 0805 apagado (o editor acende por cima)."""
    w, h = (1.0, 2.0) if vertical else (2.0, 1.0)
    d.ret_c(cx, cy, w, h, ESTANHO, rx=0.1)
    d.ret_c(cx, cy, w * (1 if vertical else 0.6), h * (0.6 if vertical else 1), '#f3efe6', rx=0.1)
    d.ret_c(cx, cy, w * (0.7 if vertical else 0.4), h * (0.4 if vertical else 0.7), '#ffd9d4', op=0.6)


def soic8(d, cx, cy, vertical=False):
    for k in range(4):
        for s in (-1, 1):
            if vertical:
                d.ret_c(cx + s * 2.95, cy - 1.905 + k * 1.27, 1.0, 0.42, ESTANHO)
            else:
                d.ret_c(cx - 1.905 + k * 1.27, cy + s * 2.95, 0.42, 1.0, ESTANHO)
    d.ret_c(cx, cy, 3.9 if vertical else 4.9, 4.9 if vertical else 3.9, '#202124', rx=0.15)
    d.circ(cx - (1.2 if vertical else 1.9), cy - (1.9 if vertical else 1.2), 0.3, '#3a3c40')


def trimpot_3296(d, cx, cy):
    """O trimpot azul 3296W de pe, visto de cima (9,5 x 4,8 mm, o parafuso de latao numa ponta)."""
    d.ret_c(cx, cy, 9.5, 4.8, '#2a64c8', rx=0.3, stroke='#173d80', sw=0.12)
    d.ret_c(cx, cy, 9.5, 4.8, 'url(#luz)', rx=0.3)
    d.circ(cx + 3.3, cy, 1.35, '#d9b45a', stroke='#8a6d1f', sw=0.12)
    d.linha(cx + 3.3 - 0.9, cy, cx + 3.3 + 0.9, cy, '#8a6d1f', 0.3)


def barra_90(d, x_borda, ys, comp):
    """A barra de pinos a 90 graus na borda direita da placa (os pinos para fora)."""
    d.ret(x_borda - 2.5, min(ys) - 1.27, 2.5, max(ys) - min(ys) + 2.54, PLASTICO, rx=0.15, stroke='#0b0c0d', sw=0.1)
    for y in ys:
        d.ret(x_borda - 1.0, y - 0.32, comp + 1.0, 0.64, 'url(#metal)', stroke='#80868d', sw=0.05)
        d.ret(x_borda + comp - 0.5, y - 0.32, 0.5, 0.64, '#e9ecef')
        d.linha(x_borda - 2.2, y, x_borda - 0.3, y, '#2c2e32', 0.25)


def barra_90_esq(d, x_borda, ys, comp):
    """A mesma barra, na borda esquerda (os pinos saindo para a esquerda)."""
    d.ret(x_borda, min(ys) - 1.27, 2.5, max(ys) - min(ys) + 2.54, PLASTICO, rx=0.15, stroke='#0b0c0d', sw=0.1)
    for y in ys:
        d.ret(x_borda - comp, y - 0.32, comp + 1.0, 0.64, 'url(#metal)', stroke='#80868d', sw=0.05)
        d.ret(x_borda - comp, y - 0.32, 0.5, 0.64, '#e9ecef')
        d.linha(x_borda + 0.3, y, x_borda + 2.2, y, '#2c2e32', 0.25)


def barra_baixo(d, y_borda, xs, comp):
    """A barra de pinos que sai pela borda de baixo, no plano da placa."""
    d.ret(min(xs) - 1.27, y_borda - 2.5, max(xs) - min(xs) + 2.54, 2.5, PLASTICO, rx=0.15, stroke='#0b0c0d', sw=0.1)
    for x in xs:
        d.ret(x - 0.32, y_borda - 1.0, 0.64, comp + 1.0, 'url(#metal-h)', stroke='#80868d', sw=0.05)
        d.ret(x - 0.32, y_borda + comp - 0.5, 0.64, 0.5, '#e9ecef')
        d.linha(x, y_borda - 2.2, x, y_borda - 0.3, '#2c2e32', 0.25)


def luz(id_peca, x, y, r, cor, chave):
    luzes.append((id_peca, x * A, y * A, r, cor, chave))


# =====================================================================
#  hc-sr04: o ultrassom, de frente
# =====================================================================
L, H = caixa(45.25, 26.3)
X0 = (L - 45.0) / 2
XS_US = [X0 + 22.5 + (k - 1.5) * PASSO for k in range(4)]
Y_US = H - 0.8
d = Desenho(T(A, 0, 0, A, 0, 0))
placa(d, X0, 0, 45.0, 20.0, '#1f5aa6', '#0f3266', rx=0.6)
for x in (X0 + 1.7, X0 + 43.3):
    for y in (1.7, 18.3):
        furo(d, x, y, r=0.8)
d.ret_c(X0 + 22.5, 3.6, 11.0, 4.6, 'url(#metal)', rx=2.3, stroke='#6c737b', sw=0.12)       # o cristal HC-49S
d.texto(X0 + 22.5, 3.6, '12.000', 0.85, '#5b6168', alinhar='center')
for k, (cx, rot) in enumerate(((X0 + 9.5, 'T'), (X0 + 35.5, 'R'))):
    d.circ(cx + 0.3, 10.4, 8.0, '#000000', op=0.3)
    d.circ(cx, 10.0, 8.0, 'url(#lata)', stroke='#6c737b', sw=0.15)                      # a lata do transdutor
    d.circ(cx, 10.0, 7.0, '#1c1d20')
    d.circ(cx, 10.0, 6.3, '#3a3d42')
    for i in range(-6, 7):                                                                 # a tela
        for j in range(-6, 7):
            px, py = i * 1.0 + (0.5 if j % 2 else 0), j * 0.87
            if px * px + py * py < 5.6 ** 2:
                d.circ(cx + px, 10.0 + py, 0.26, '#141517')
    d.circ(cx, 10.0, 6.3, 'url(#cupula)', op=0.5)
    d.texto(X0 + (16.3 if k == 0 else 28.7), 16.0, rot, 1.2, BRANCO, alinhar='center')
d.texto(X0 + 22.5, 8.6, 'HC-SR04', 1.25, BRANCO, alinhar='center')
for x, n in zip(XS_US, ('VCC', 'Trig', 'Echo', 'Gnd')):
    d.texto(x, 13.6, n, 0.7, BRANCO, ang=90, alinhar='center')
barra_baixo(d, 20.0, XS_US, H - 0.15 - 20.0)
gravar('hc-sr04', d, L, H)
guardar('hc-sr04', L, H, [
    ('VCC', XS_US[0], Y_US, ['power'], 'Alimentacao: 5 V'),
    ('TRIG', XS_US[1], Y_US, [], 'Disparo: um pulso de 10 us em HIGH manda o som'),
    ('ECHO', XS_US[2], Y_US, [], 'Eco: fica em HIGH o tempo da ida e volta do som (58 us por cm). Em 5 V: no ESP32, use um divisor'),
    ('GND', XS_US[3], Y_US, ['power'], 'GND')])

# =====================================================================
#  dht22 (AM2302) e dht11: os sensores soltos
# =====================================================================


def dht(nome, larg, alt, cor, borda, furos_grade, rotulo, cor_rotulo, pinos):
    L, H = caixa(larg + 0.4, alt + 6.9)
    cx = L / 2
    xs = [cx + (k - 1.5) * PASSO for k in range(4)]
    d = Desenho(T(A, 0, 0, A, 0, 0))
    for x in xs:
        d.ret(x - 0.25, alt - 0.5, 0.5, H - 0.15 - (alt - 0.5), 'url(#metal-h)', stroke='#7c838b', sw=0.05)
    d.ret_c(cx, alt / 2 + 0.1, larg, alt, cor, rx=0.8, stroke=borda, sw=0.15)
    d.ret_c(cx, alt / 2 + 0.1, larg, alt, 'url(#luz)', rx=0.8)
    furos_grade(d, cx)
    if rotulo:
        d.texto(cx, alt - 2.2, rotulo, 1.0, cor_rotulo, alinhar='center')
    gravar(nome, d, L, H)
    guardar(nome, L, H, [(n, x, H - 0.75, s, dsc) for (n, s, dsc), x in zip(pinos, xs)])


def grade_dht22(d, cx):
    d.ret_c(cx, 2.6, 6.0, 3.6, '#e4e4de', rx=0.6, stroke='#c9c9c2', sw=0.1)                 # a aba de cima, com o furo
    d.circ(cx, 2.6, 1.55, '#2b2c2f')
    for linha in range(8):
        y = 6.6 + linha * 1.75
        for col in range(3):
            x = cx - 4.6 + col * 4.6 + (1.15 if linha % 2 else -1.15)
            d.ret_c(x, y, 2.7, 0.95, '#2b2c2f', rx=0.25)


def grade_dht11(d, cx):
    for linha in range(5):
        for col in range(4):
            d.ret_c(cx - 3.3 + col * 2.2, 2.0 + linha * 2.2, 1.45, 1.45, '#173f6e', rx=0.15)


dht('dht22', 15.1, 25.0, '#f1f1ec', '#b9b9b2', grade_dht22, 'AM2302', '#55575c', [
    ('VCC', ['power'], 'Alimentacao: 3,3 a 6 V'),
    ('SDA', [], 'Os dados (um fio so): no pino digital, com pull-up de 10k'),
    ('NC', [], 'Nao ligado'),
    ('GND', ['power'], 'GND')])
dht('dht11', 12.0, 15.5, '#2f7ccc', '#17457a', grade_dht11, 'DHT11', '#e8f0fa', [
    ('VCC', ['power'], 'Alimentacao: 3,3 a 5,5 V'),
    ('DATA', [], 'Os dados (um fio so): no pino digital, com pull-up de 10k'),
    ('NC', [], 'Nao ligado'),
    ('GND', ['power'], 'GND')])

# =====================================================================
#  os modulos do LM393: o do LDR e o da chama
# =====================================================================


def modulo_lm393(nome, sensor, nomes, descricoes, x_placa):
    """A placa azul de 32 x 14 mm com a barra de quatro pinos na direita."""
    L, H = caixa(x_placa + 32.0 + 6.2, 14.4)
    X0, Y0 = x_placa, (H - 14.0) / 2
    cy = H / 2
    ys = [cy + (k - 1.5) * PASSO for k in range(4)]
    d = Desenho(T(A, 0, 0, A, 0, 0))
    placa(d, X0, Y0, 32.0, 14.0, AZUL, AZUL_BORDA)
    furo(d, X0 + 9.6, cy, r=1.5)
    sensor(d, X0, cy)
    trimpot_3296(d, X0 + 16.4, Y0 + 3.4)
    soic8(d, X0 + 16.6, Y0 + 10.2)
    for k, y in enumerate((Y0 + 2.4, Y0 + 11.6)):
        smd(d, X0 + 22.0, y, 1.6, 0.8, '#1a1a1a')
    for y in (cy - 1.3, cy + 1.3):
        smd(d, X0 + 22.0, y, 1.6, 0.8, '#a37a52')
    led_smd(d, X0 + 25.2, Y0 + 2.4)
    led_smd(d, X0 + 25.2, Y0 + 11.6)
    d.texto(X0 + 25.2, Y0 + 4.2, 'PWR', 0.6, BRANCO, alinhar='center')
    d.texto(X0 + 25.2, Y0 + 9.8, 'DO', 0.6, BRANCO, alinhar='center')
    for n, y in zip(nomes, ys):
        d.texto(X0 + 29.2, y, n, 0.75, BRANCO, alinhar='center-right')
    barra_90(d, X0 + 32.0, ys, L - 0.15 - (X0 + 32.0))
    gravar(nome, d, L, H)
    guardar(nome, L, H, [(n, L - 0.9, y, s, dsc) for n, y, (s, dsc) in zip(nomes, ys, descricoes)])
    luz(nome, X0 + 25.2, Y0 + 2.4, 0.55, 'vermelho', 'vcc-gnd')
    luz(nome, X0 + 25.2, Y0 + 11.6, 0.55, 'vermelho', 'sensor-do')


def ldr(d, x0, cy):
    """O LDR de 5 mm deitado na ponta da placa: a ceramica e a trilha em zigue-zague."""
    cx = x0 + 3.6
    d.circ(cx, cy, 2.6, '#d9c9a6', stroke='#9b8a68', sw=0.12)
    d.circ(cx, cy, 2.25, '#efe4cc')
    pts = []
    for k in range(7):
        y = cy - 1.6 + k * 0.53
        x_a, x_b = cx - 1.7, cx + 1.7
        pts += [(x_a, y), (x_b, y)] if k % 2 == 0 else [(x_b, y), (x_a, y)]
    d.poli(pts, None, stroke='#b5452e', sw=0.28, fechado=False)
    d.circ(cx, cy, 2.25, 'url(#cupula)', op=0.6)


def receptor_ir(d, x0, cy):
    """O fotodiodo infravermelho preto de 5 mm, deitado para fora da placa (para a esquerda)."""
    for s in (-1, 1):
        d.linha(x0 + 2.4, cy + s * 1.27, x0 - 1.2, cy + s * 1.27, '#c3c8cd', 0.45)
        d.circ(x0 + 2.4, cy + s * 1.27, 0.75, ESTANHO)
    d.ret(x0 - 1.4, cy - 2.9, 1.0, 5.8, '#121315', rx=0.2)                                 # o colar
    d.caminho([('M', x0 - 1.4, cy - 2.5), ('L', x0 - 6.4, cy - 2.5), ('A', 2.5, 0, 0, x0 - 6.4, cy + 2.5),
               ('L', x0 - 1.4, cy + 2.5), ('Z',)], '#18191c', stroke='#050506', sw=0.1)
    d.ret(x0 - 6.6, cy - 1.9, 4.6, 0.7, '#ffffff', rx=0.3, op=0.18)


desc_dig = 'Saida digital do comparador: muda quando passa do ponto do trimpot (o LED DO acende)'
modulo_lm393('photoresistor-sensor', ldr, ['VCC', 'GND', 'DO', 'AO'],
             [(['power'], 'Alimentacao: 3,3 a 5 V'), (['power'], 'GND'),
              ([], 'Saida digital: LOW com a luz acima do ponto do trimpot (o LED DO acende)'),
              (['analog'], 'Saida analogica: mais luz, menos tensao')], 0.2)
modulo_lm393('flame-sensor', receptor_ir, ['VCC', 'GND', 'DOUT', 'AOUT'],
             [(['power'], 'Alimentacao: 3,3 a 5 V'), (['power'], 'GND'),
              ([], 'Saida digital: LOW com chama (o LED DO acende)'),
              (['analog'], 'Saida analogica: mais chama, menos tensao')], 9.4)

# =====================================================================
#  gas-sensor: o modulo do MQ-2
# =====================================================================
L, H = caixa(32.2 + 6.2, 22.4)
X0, Y0 = 0.2, (H - 22.0) / 2
cy = H / 2
NOMES_MQ = ['AOUT', 'DOUT', 'GND', 'VCC']
YS_MQ = [cy + (k - 1.5) * PASSO for k in range(4)]
d = Desenho(T(A, 0, 0, A, 0, 0))
placa(d, X0, Y0, 32.0, 22.0, AZUL, AZUL_BORDA)
for y in (Y0 + 2.4, Y0 + 19.6):
    furo(d, X0 + 2.4, y, r=1.4)
CX_MQ = X0 + 13.6
d.circ(CX_MQ + 0.4, cy + 0.5, 10.0, '#000000', op=0.3)
d.circ(CX_MQ, cy, 10.0, '#2a2523', stroke='#0d0c0b', sw=0.15)                                # a base de baquelite
d.circ(CX_MQ, cy, 8.6, 'url(#tela-aco)', stroke='#5d646b', sw=0.2)                           # a tela de aco
for k in range(1, 9):                                                                         # a trama
    off = -8.0 + k * 1.78
    meia = math.sqrt(max(0.0, 8.2 ** 2 - off ** 2))
    d.linha(CX_MQ + off, cy - meia, CX_MQ + off, cy + meia, '#8d949b', 0.12)
    d.linha(CX_MQ - meia, cy + off, CX_MQ + meia, cy + off, '#8d949b', 0.12)
d.circ(CX_MQ, cy, 8.6, None, stroke='#c9ced3', sw=0.5)
d.circ(CX_MQ, cy, 8.6, 'url(#cupula)', op=0.35)
led_smd(d, X0 + 26.0, Y0 + 3.0)
led_smd(d, X0 + 26.0, Y0 + 19.0)
d.texto(X0 + 26.0, Y0 + 4.9, 'PWR', 0.6, BRANCO, alinhar='center')
d.texto(X0 + 26.0, Y0 + 17.1, 'DO', 0.6, BRANCO, alinhar='center')
d.texto(X0 + 25.2, cy, 'MQ-2', 0.9, BRANCO, ang=90, alinhar='center')
for n, y in zip(NOMES_MQ, YS_MQ):
    d.texto(X0 + 29.2, y, n.replace('OUT', 'O'), 0.75, BRANCO, alinhar='center-right')
barra_90(d, X0 + 32.0, YS_MQ, L - 0.15 - (X0 + 32.0))
gravar('gas-sensor', d, L, H)
guardar('gas-sensor', L, H, [
    ('AOUT', L - 0.9, YS_MQ[0], ['analog'], 'Saida analogica: mais gas, mais tensao'),
    ('DOUT', L - 0.9, YS_MQ[1], [], 'Saida digital: LOW com gas acima do ponto do trimpot (o LED DO acende)'),
    ('GND', L - 0.9, YS_MQ[2], ['power'], 'GND'),
    ('VCC', L - 0.9, YS_MQ[3], ['power'], 'Alimentacao: 5 V (o aquecedor do MQ-2 gasta ~150 mA)')])
luz('gas-sensor', X0 + 26.0, Y0 + 3.0, 0.55, 'vermelho', 'vcc-gnd')
luz('gas-sensor', X0 + 26.0, Y0 + 19.0, 0.55, 'vermelho', 'sensor-do')

# =====================================================================
#  os KY de 18,5 x 15 mm: o KY-013 (NTC) e o KY-039 (batimento)
# =====================================================================


def ky_tres(nome, rotulo, desenho, descricoes):
    L, H = caixa(24.6, 15.35)
    X0, Y0 = 0.2, (H - 15.0) / 2
    cy = H / 2
    ys = [cy - PASSO, cy, cy + PASSO]
    d = Desenho(T(A, 0, 0, A, 0, 0))
    placa(d, X0, Y0, 18.5, 15.0, PRETA, PRETA_BORDA)
    for k, y in enumerate(ys):
        ilha(d, X0 + 14.6, y, quadrada=k == 2)
    d.texto(X0 + 12.9, ys[0], '-', 1.1, BRANCO, alinhar='center')
    d.texto(X0 + 12.9, ys[2], 'S', 1.0, BRANCO, alinhar='center')
    d.texto(X0 + 5.0, Y0 + 14.0, rotulo, 0.65, BRANCO, alinhar='center')
    desenho(d, X0, cy)
    barra_90(d, X0 + 18.5, ys, L - 0.15 - (X0 + 18.5))
    gravar(nome, d, L, H)
    guardar(nome, L, H, [(n, L - 0.9, y, s, dsc) for (n, s, dsc), y in zip(descricoes, ys)])


def ntc(d, x0, cy):
    furo(d, x0 + 2.6, cy - 4.4, r=1.3)
    for y in (cy - 0.9, cy + 0.9):                                    # as pernas do NTC, soldadas
        d.circ(x0 + 9.4, y, 0.6, ESTANHO)
        d.circ(x0 + 9.4, y, 0.3, '#e3e6e9')
    d.linha(x0 + 4.4, cy - 0.35, x0 + 9.4, cy - 0.9, '#c3c8cd', 0.3)
    d.linha(x0 + 4.4, cy + 0.35, x0 + 9.4, cy + 0.9, '#c3c8cd', 0.3)
    d.circ(x0 + 3.6, cy, 1.35, '#1a1b1d', stroke='#050506', sw=0.1)  # a gota de epoxi
    d.circ(x0 + 3.2, cy - 0.45, 0.4, '#ffffff', op=0.25)
    smd(d, x0 + 10.5, cy + 4.6, 1.6, 0.8, '#1a1a1a')
    d.texto(x0 + 10.5, cy + 5.8, '10k', 0.55, '#c9ccd0', alinhar='center')


def batimento(d, x0, cy):
    """O LED infravermelho (transparente, 5 mm) e o fototransistor preto, de pe."""
    for cx, cor in ((x0 + 4.4, '#dfe8f0'), (x0 + 10.2, '#202124')):
        d.circ(cx + 0.25, cy + 0.3, 2.6, '#000000', op=0.3)
        d.circ(cx, cy, 2.6, cor, stroke='#55595f', sw=0.12)
        d.circ(cx, cy, 2.1, 'url(#cupula)', op=0.7)
    d.circ(x0 + 4.4, cy, 0.8, '#a9b6c4', op=0.8)
    smd(d, x0 + 10.5, cy + 4.9, 1.6, 0.8, '#1a1a1a')


ky_tres('ntc-temperature-sensor', 'KY-013', ntc, [
    ('GND', ['power'], 'GND (- na placa)'),
    ('VCC', ['power'], 'Alimentacao (o pino do meio): 3,3 V ou 5 V'),
    ('OUT', ['analog'], 'Saida analogica (S na placa): o divisor do NTC de 10k com o resistor de 10k')])
ky_tres('heart-beat-sensor', 'KY-039', batimento, [
    ('GND', ['power'], 'GND (- na placa)'),
    ('VCC', ['power'], 'Alimentacao (o pino do meio): 3,3 V ou 5 V'),
    ('OUT', ['analog'], 'Saida analogica (S na placa): a luz do LED que passa pelo dedo')])

# =====================================================================
#  os sensores de som: o KY-037 (microfone grande) e o KY-038 (pequeno)
# =====================================================================


def som(nome, rotulo, r_mic):
    L, H = caixa(6.2 + 35.0 + 0.2, 15.4)
    X0, Y0 = 6.2, (H - 15.0) / 2
    cy = H / 2
    nomes = ['AOUT', 'GND', 'VCC', 'DOUT']
    ys = [cy + (k - 1.5) * PASSO for k in range(4)]
    d = Desenho(T(A, 0, 0, A, 0, 0))
    placa(d, X0, Y0, 35.0, 15.0, VERMELHA, VERMELHA_BORDA)
    furo(d, X0 + 7.6, Y0 + 2.4, r=1.4)
    furo(d, X0 + 7.6, Y0 + 12.6, r=1.4)
    for n, y in zip(('AO', 'G', '+', 'DO'), ys):
        d.texto(X0 + 3.2, y, n, 0.8, BRANCO, alinhar='center-left')
    trimpot_3296(d, X0 + 15.6, Y0 + 3.3)
    soic8(d, X0 + 15.4, Y0 + 10.6)
    led_smd(d, X0 + 22.0, Y0 + 2.2)
    led_smd(d, X0 + 22.0, Y0 + 12.8)
    d.texto(X0 + 23.8, Y0 + 2.2, 'L1', 0.6, BRANCO, alinhar='center-left')
    d.texto(X0 + 23.8, Y0 + 12.8, 'L2', 0.6, BRANCO, alinhar='center-left')
    for y in (cy - 1.8, cy + 0.6):
        smd(d, X0 + 22.0, y, 1.6, 0.8, '#1a1a1a')
    cx = X0 + 35.0 - 1.4 - r_mic
    d.circ(cx + 0.3, cy + 0.35, r_mic, '#000000', op=0.3)
    d.circ(cx, cy, r_mic, 'url(#lata)', stroke='#6c737b', sw=0.15)                         # o microfone de eletreto
    d.circ(cx, cy, r_mic - 0.55, '#1c1d20')
    for k in range(int(r_mic * 2.2)):                                                       # o feltro
        a = 2 * math.pi * k / int(r_mic * 2.2)
        d.circ(cx + (r_mic - 1.3) * 0.5 * math.cos(a), cy + (r_mic - 1.3) * 0.5 * math.sin(a), 0.18, '#2b2d31')
    d.texto(X0 + 9.3, Y0 + 7.5, rotulo, 0.6, BRANCO, ang=90, alinhar='center')
    barra_90_esq(d, X0, ys, X0 - 0.15)
    gravar(nome, d, L, H)
    desc = {'AOUT': (['analog'], 'Saida analogica: o som do microfone'), 'GND': (['power'], 'GND (G na placa)'),
            'VCC': (['power'], 'Alimentacao (+ na placa): 3,3 V ou 5 V'),
            'DOUT': ([], 'Saida digital: HIGH com o som acima do ponto do trimpot (o L2 acende)')}
    guardar(nome, L, H, [(n, 0.9, y, *desc[n]) for n, y in zip(nomes, ys)])
    luz(nome, X0 + 22.0, Y0 + 2.2, 0.55, 'vermelho', 'vcc-gnd')
    luz(nome, X0 + 22.0, Y0 + 12.8, 0.55, 'vermelho', 'sensor-do')


som('big-sound-sensor', 'KY-037', 4.85)
som('small-sound-sensor', 'KY-038', 3.0)

# =====================================================================
#  pir-motion-sensor: o HC-SR501, de frente
# =====================================================================
L, H = caixa(32.4, 29.4)
X0 = (L - 32.0) / 2
XS_PIR = [X0 + 16.0 + (k - 1) * PASSO for k in range(3)]
d = Desenho(T(A, 0, 0, A, 0, 0))
for x in XS_PIR:                                                      # as pernas da barra, embaixo
    d.ret(x - 0.32, 23.0, 0.64, H - 0.15 - 23.0, 'url(#metal-h)', stroke='#80868d', sw=0.05)
placa(d, X0, 0, 32.0, 24.0, '#2a7a3e', '#174a25', rx=0.6)
for x in (X0 + 2.0, X0 + 30.0):                                       # os furos M2 a 28 mm
    furo(d, x, 12.0, r=1.0)
d.ret_c(X0 + 16.0, 12.0, 24.0, 24.0, '#e9ebe8', rx=0.8, stroke='#b4b8b4', sw=0.15)    # a aba da lente
d.circ(X0 + 16.2, 12.3, 11.5, '#000000', op=0.15)
d.circ(X0 + 16.0, 12.0, 11.5, 'url(#lente)', stroke='#b9bdba', sw=0.15)               # a lente de Fresnel
for k in range(1, 6):
    d.circ(X0 + 16.0, 12.0, k * 2.0, None, stroke='#cfd3d0', sw=0.12)
for k in range(12):
    a = 2 * math.pi * k / 12
    d.linha(X0 + 16.0 + 2.0 * math.cos(a), 12.0 + 2.0 * math.sin(a),
            X0 + 16.0 + 11.2 * math.cos(a), 12.0 + 11.2 * math.sin(a), '#d4d8d5', 0.1)
d.circ(X0 + 16.0, 12.0, 11.5, 'url(#cupula)', op=0.45)
gravar('pir-motion-sensor', d, L, H)
guardar('pir-motion-sensor', L, H, [
    ('VCC', XS_PIR[0], H - 0.8, ['power'], 'Alimentacao: 5 a 20 V (o regulador da placa faz os 3,3 V)'),
    ('OUT', XS_PIR[1], H - 0.8, [], 'Saida: 3,3 V com movimento (pelo tempo do trimpot), LOW parado'),
    ('GND', XS_PIR[2], H - 0.8, ['power'], 'GND')])

# =====================================================================
#  mpu6050: o GY-521
# =====================================================================
L, H = caixa(21.2, 16.2)
X0, Y0 = 0.1, 0.1
NOMES_GY = ['VCC', 'GND', 'SCL', 'SDA', 'XDA', 'XCL', 'AD0', 'INT']
XS_GY = [X0 + 10.5 + (k - 3.5) * PASSO for k in range(8)]
Y_GY = Y0 + 1.6
d = Desenho(T(A, 0, 0, A, 0, 0))
placa(d, X0, Y0, 21.0, 16.0, '#1f4fb0', '#102c6a', rx=0.6)
for x in (X0 + 2.6, X0 + 18.4):
    furo(d, x, Y0 + 13.4, r=1.5)
for k, (x, n) in enumerate(zip(XS_GY, NOMES_GY)):
    ilha(d, x, Y_GY, quadrada=k == 0)
    d.texto(x, Y0 + 4.2, n, 0.62, BRANCO, ang=90, alinhar='center')
d.ret_c(X0 + 10.5, Y0 + 10.4, 4.0, 4.0, '#202124', rx=0.15)                                  # o MPU-6050 (QFN de 4 x 4)
for k in range(6):
    for s in (-1, 1):
        d.ret_c(X0 + 10.5 - 1.25 + k * 0.5, Y0 + 10.4 + s * 2.15, 0.25, 0.3, ESTANHO)
        d.ret_c(X0 + 10.5 + s * 2.15, Y0 + 10.4 - 1.25 + k * 0.5, 0.3, 0.25, ESTANHO)
d.circ(X0 + 9.0, Y0 + 8.9, 0.22, '#3a3c40')
d.ret_c(X0 + 5.2, Y0 + 9.0, 1.6, 2.9, '#202124')                                             # o regulador (SOT-23-5)
for k in range(3):
    d.ret_c(X0 + 3.95, Y0 + 8.05 + k * 0.95, 0.9, 0.4, ESTANHO)
for k in range(2):
    d.ret_c(X0 + 6.45, Y0 + 8.05 + k * 1.9, 0.9, 0.4, ESTANHO)
for x, y, cor in ((X0 + 15.0, Y0 + 7.6, '#a37a52'), (X0 + 15.0, Y0 + 9.4, '#a37a52'), (X0 + 16.9, Y0 + 7.6, '#1a1a1a'),
                  (X0 + 16.9, Y0 + 9.4, '#1a1a1a'), (X0 + 15.0, Y0 + 11.6, '#a37a52'), (X0 + 9.0, Y0 + 13.8, '#a37a52')):
    smd(d, x, y, 1.2, 0.6, cor, vertical=True)
led_smd(d, X0 + 6.6, Y0 + 13.8)
gravar('mpu6050', d, L, H)
DESC_GY = {'VCC': (['power'], 'Alimentacao: 3,3 a 5 V (o regulador da placa faz os 3,3 V)'), 'GND': (['power'], 'GND'),
           'SCL': ([], 'I2C: o relogio (SCL da placa)'), 'SDA': ([], 'I2C: os dados (SDA da placa)'),
           'XDA': ([], 'I2C auxiliar: dados de um sensor de fora (magnetometro)'), 'XCL': ([], 'I2C auxiliar: o relogio'),
           'AD0': ([], 'O endereco: LOW (ou solto) = 0x68, HIGH = 0x69'), 'INT': ([], 'Interrupcao: avisa que ha dado novo')}
guardar('mpu6050', L, H, [(n, x, Y_GY, *DESC_GY[n]) for n, x in zip(NOMES_GY, XS_GY)])
luz('mpu6050', X0 + 6.6, Y0 + 13.8, 0.55, 'vermelho', 'vcc-gnd')

# =====================================================================
#  hx711: o modulo verde, com os fios da celula de carga
# =====================================================================
L, H = caixa(38.2 + 10.0, 21.4)
X0, Y0 = 0.2, (H - 21.0) / 2
cy = H / 2
NOMES_HX = ['GND', 'DT', 'SCK', 'VCC']
YS_HX = [cy + (k - 1.5) * PASSO for k in range(4)]
YS_CEL = [cy + (k - 2.5) * PASSO for k in range(6)]
d = Desenho(T(A, 0, 0, A, 0, 0))
placa(d, X0, Y0, 38.0, 21.0, VERDE, VERDE_BORDA)
for k, y in enumerate(YS_HX):
    ilha(d, X0 + 1.9, y, quadrada=k == 0)
    d.texto(X0 + 3.4, y, NOMES_HX[k], 0.75, BRANCO, alinhar='center-left')
for k, (y, n) in enumerate(zip(YS_CEL, ('E+', 'E-', 'A-', 'A+', 'B-', 'B+'))):
    ilha(d, X0 + 36.1, y, quadrada=k == 0)
    d.texto(X0 + 34.4, y, n, 0.75, BRANCO, alinhar='center-right')
d.ret_c(X0 + 19.0, cy, 10.0, 4.0, '#202124', rx=0.15)                                      # o HX711 (SOP-16)
for k in range(8):
    for s in (-1, 1):
        d.ret_c(X0 + 19.0 - 4.445 + k * 1.27, cy + s * 2.6, 0.42, 1.2, ESTANHO)
d.texto(X0 + 19.0, cy, 'HX711', 0.9, '#c8cbd0', alinhar='center')
d.circ(X0 + 14.6, cy + 1.2, 0.3, '#3a3c40')
d.ret_c(X0 + 26.8, Y0 + 4.0, 2.9, 1.6, '#202124')                                          # o transistor do E+
for x, y, cor in ((X0 + 9.0, Y0 + 4.0, '#a37a52'), (X0 + 11.2, Y0 + 4.0, '#1a1a1a'), (X0 + 9.0, Y0 + 17.0, '#a37a52'),
                  (X0 + 11.2, Y0 + 17.0, '#1a1a1a'), (X0 + 26.8, Y0 + 17.0, '#a37a52'), (X0 + 29.0, Y0 + 17.0, '#1a1a1a'),
                  (X0 + 29.4, Y0 + 4.0, '#a37a52')):
    smd(d, x, y, 1.6, 0.8, cor, vertical=True)
d.texto(X0 + 19.0, Y0 + 19.6, 'Load Cell Amp', 0.6, BRANCO, alinhar='center')
# os fios da celula de carga: vermelho no E+, preto no E-, branco no A-, verde no A+
for y, cor in zip(YS_CEL[:4], ('#d0312d', '#1b1b1b', '#eeeeea', '#2e9a3e')):
    d.linha(X0 + 36.1, y, L - 2.6, cy - 1.8 + (YS_CEL.index(y)) * 1.2, cor, 0.7, curva=0)
for k, cor in enumerate(('#d0312d', '#1b1b1b', '#eeeeea', '#2e9a3e')):
    d.linha(L - 2.7, cy - 1.8 + k * 1.2, L - 0.4, cy - 1.8 + k * 1.2, cor, 0.7, cap='butt')
d.ret(L - 2.9, cy - 2.6, 1.2, 4.8, '#2b2d31', rx=0.3)                                       # o termorretratil da celula
d.texto(L - 2.3, cy + 4.4, 'celula', 0.55, BRANCO, alinhar='center')
gravar('hx711', d, L, H)
DESC_HX = {'GND': (['power'], 'GND'), 'DT': ([], 'Os dados (DOUT do HX711)'), 'SCK': ([], 'O relogio (PD_SCK do HX711)'),
           'VCC': (['power'], 'Alimentacao: 2,6 a 5,5 V')}
guardar('hx711', L, H, [(n, X0 + 1.9, y, *DESC_HX[n]) for n, y in zip(NOMES_HX, YS_HX)])

# =====================================================================
cam = pa.BASE + 'app/assets/pecas.json'
dados = json.load(io.open(cam, encoding='utf-8'))
novos = {p['id'] for p in pecas}
dados = [p for p in dados if p['id'] not in novos] + pecas
dados.sort(key=lambda p: p['id'])
io.open(cam, 'w', encoding='utf-8', newline='').write(json.dumps(dados, ensure_ascii=False, indent=1))
print(f'\ncatalogo: {len(dados)} pecas\n\nluzes (EditorModulos.cs):')
for p, x, y, r, cor, chave in luzes:
    print(f'  {p}: ({x:.2f}, {y:.2f}, {r} * Mm, {cor}, "{chave}")')
