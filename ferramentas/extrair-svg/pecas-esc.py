"""python ferramentas/extrair-svg/pecas-esc.py

As ESCs, com a cor e a corrente escolhidas na barra da peca:

  - esc-<A>a-<cor>      : a ESC simples (o mesmo desenho do pecas-rc.py, que
                          continua gerando o "esc" padrao: 30 A, azul), com a
                          corrente no rotulo e o termo-retratil na cor.
  - esc-4em1            : a ESC 4-em-1 de drone (placa de 40 x 40 mm, furacao
                          de 30,5 mm): quatro motores, uma bateria, os quatro
                          sinais e o GND no conector de cima.
  - esc-4em1-<A>a-<cor> : as variantes dela.

So as variantes de desenho; o catalogo ganha so a esc-4em1.
"""
import io
import json

BASE = 'C:/Users/seraf/OneDrive/Desktop/SimuladorESP32/'
PX = 96 / 25.4
PASSO = 2.54

# id -> (corpo, borda, letras claras)
CORES = {
    'azul': ('#1f5fb4', '#123e7a', '#cfe0f7'),
    'preto': ('#26282c', '#0e0f11', '#c9ccd1'),
    'vermelho': ('#c0392b', '#7b2219', '#f6d5d1'),
    'verde': ('#1e8449', '#114d2a', '#d1f2de'),
    'roxo': ('#6c3483', '#40204e', '#e8d6f0'),
    'laranja': ('#d35400', '#7e3200', '#fde3cf'),
    'rosa': ('#d6337a', '#831f4b', '#fbd6e6'),
}
AMPERES_SIMPLES = [12, 20, 30, 40, 60]
AMPERES_4EM1 = [35, 45, 60]


def gravar_svg(nome, l, a, corpo):
    svg = ('<svg xmlns="http://www.w3.org/2000/svg" version="1.1"' + chr(10)
           + f'     width="{l}mm" height="{a}mm" viewBox="0 0 {l} {a}">' + chr(10)
           + corpo + chr(10) + '</svg>' + chr(10))
    io.open(BASE + f'app/assets/{nome}.svg', 'w', encoding='utf-8', newline='').write(svg)


def fio(d, cor, borda, largura=0.9):
    return (f'  <path d="{d}" fill="none" stroke="{borda}" stroke-width="{largura + 0.35:.2f}" stroke-linecap="round"/>\n'
            f'  <path d="{d}" fill="none" stroke="{cor}" stroke-width="{largura:.2f}" stroke-linecap="round"/>\n')


def ponta(x, y):
    return (f'  <circle cx="{x:.2f}" cy="{y:.2f}" r="0.75" fill="#d9b44a" stroke="#8a6d1f" stroke-width="0.2"/>\n'
            f'  <circle cx="{x:.2f}" cy="{y:.2f}" r="0.32" fill="#2a2a2a"/>\n')


def texto(x, y, t, tam=1.2, cor='#f2f2f2', ancora='middle'):
    return (f'  <text x="{x:.2f}" y="{y:.2f}" fill="{cor}" font-size="{tam}px" text-anchor="{ancora}"\n'
            f'        font-family="Verdana" font-weight="bold">{t}</text>\n')


# =====================================================================
#  ESC simples (o mesmo desenho do pecas-rc.py)
# =====================================================================
def esc_simples(amperes, cor):
    corpo_cor, borda_cor, letra = CORES[cor]
    l, a = 66.0, 30.0
    x0, x1, y0, y1 = 14.0, 52.0, 6.0, 24.0
    xs_motor = 1.5
    ys_motor = [a / 2 - PASSO * 2, a / 2, a / 2 + PASSO * 2]
    x_dir = l - 1.5
    ys_bat = [6.0, 6.0 + 2 * PASSO]
    ys_servo = [18.7, 18.7 + PASSO, 18.7 + 2 * PASSO]
    corpo = ''
    for y in ys_motor:
        corpo += fio(f'M{x0:.2f} {a / 2 + (y - a / 2) * 0.3:.2f} C{x0 - 6:.2f} {a / 2 + (y - a / 2) * 0.3:.2f} {xs_motor + 5:.2f} {y:.2f} {xs_motor:.2f} {y:.2f}',
                     '#2b2d31', '#7a808a', 1.2)
    for y, (c, b) in zip(ys_bat, [('#d23b33', '#7d1f1a'), ('#2b2d31', '#7a808a')]):
        corpo += fio(f'M{x1:.2f} {9.0 + (y - 6.0) * 0.4:.2f} C{x1 + 6:.2f} {9.0 + (y - 6.0) * 0.4:.2f} {x_dir - 5:.2f} {y:.2f} {x_dir:.2f} {y:.2f}',
                     c, b, 1.3)
    for y, (c, b) in zip(ys_servo, [('#f2f2ee', '#8f8f8a'), ('#d23b33', '#7d1f1a'), ('#7a4a2a', '#3d2515')]):
        corpo += fio(f'M{x1:.2f} {20.0 + (y - 18.7) * 0.4:.2f} C{x1 + 6:.2f} {20.0 + (y - 18.7) * 0.4:.2f} {x_dir - 5:.2f} {y:.2f} {x_dir:.2f} {y:.2f}',
                     c, b, 0.7)
    for y in ys_motor:
        corpo += ponta(xs_motor, y)
    for y in ys_bat + ys_servo:
        corpo += ponta(x_dir, y)
    corpo += (f'  <rect x="{x0}" y="{y0}" width="{x1 - x0}" height="{y1 - y0}" rx="2.4" fill="{corpo_cor}" stroke="{borda_cor}" stroke-width="0.35"/>\n'
              f'  <rect x="{x0 + 1.2:.2f}" y="{y0 + 1.0:.2f}" width="{x1 - x0 - 2.4:.2f}" height="2.0" rx="1.0" fill="#ffffff" fill-opacity="0.12"/>\n'
              f'  <rect x="{x0 + 6.0:.2f}" y="{y0 + 4.2:.2f}" width="{x1 - x0 - 12.0:.2f}" height="9.6" rx="0.8" fill="#f2f2ee"/>\n')
    corpo += texto((x0 + x1) / 2, y0 + 9.6, f'{amperes}A', 3.4, corpo_cor)
    corpo += texto((x0 + x1) / 2, y0 + 12.6, 'BRUSHLESS ESC', 1.25, corpo_cor)
    corpo += texto(x0 + 2.4, a / 2 - 6.6, 'A B C', 1.0, letra, 'start')
    corpo += texto(x1 - 1.0, 4.6, 'BAT', 1.1, letra, 'end')
    corpo += texto(x1 - 1.0, 27.4, 'SERVO', 1.1, letra, 'end')
    for y, n in zip(ys_motor, 'ABC'):
        corpo += texto(xs_motor + 1.6, y - 1.0, n, 1.1, '#e6ecf5', 'start')
    for y, n in zip(ys_bat, ['+', '-']):
        corpo += texto(x_dir - 1.8, y - 0.9, n, 1.5, '#e6ecf5', 'end')
    for y, n in zip(ys_servo, ['S', '5V', 'G']):
        corpo += texto(x_dir - 1.8, y + 0.4, n, 0.9, '#e6ecf5', 'end')
    gravar_svg(f'esc-{amperes}a-{cor}', l, a, corpo)


for amperes in AMPERES_SIMPLES:
    for cor in CORES:
        esc_simples(amperes, cor)
print(f'esc simples: {len(AMPERES_SIMPLES) * len(CORES)} variantes')


# =====================================================================
#  ESC 4-em-1 (placa de drone, 40 x 40 mm)
# =====================================================================
L = A = 40.0
X_ESQ, X_DIR = 1.5, L - 1.5
YS_CIMA = [6.0, 6.0 + PASSO, 6.0 + 2 * PASSO]           # M1 (esq), M2 (dir)
YS_BAIXO = [A - 6.0 - 2 * PASSO, A - 6.0 - PASSO, A - 6.0]   # M4 (esq), M3 (dir)
Y_CONECTOR = 1.5
XS_CONECTOR = [L / 2 - 2 * PASSO + i * PASSO for i in range(5)]   # S1..S4, GND
Y_BAT = A - 1.5
XS_BAT = [L / 2 - 3.0, L / 2 + 3.0]


def esc_4em1(amperes, cor, nome):
    placa, borda, letra = CORES[cor]
    c = ''
    # a placa, com os quatro furos de 30,5 mm
    c += f'  <rect x="0.6" y="0.6" width="{L - 1.2}" height="{A - 1.2}" rx="3.0" fill="{placa}" stroke="{borda}" stroke-width="0.5"/>\n'
    c += f'  <rect x="2.2" y="2.2" width="{L - 4.4}" height="{A - 4.4}" rx="2.0" fill="none" stroke="#ffffff" stroke-opacity="0.10" stroke-width="0.4"/>\n'
    for fx in (L / 2 - 15.25, L / 2 + 15.25):
        for fy in (A / 2 - 15.25, A / 2 + 15.25):
            c += f'  <circle cx="{fx:.2f}" cy="{fy:.2f}" r="1.9" fill="#c9a43a" stroke="#7d6420" stroke-width="0.3"/>\n'
            c += f'  <circle cx="{fx:.2f}" cy="{fy:.2f}" r="1.1" fill="#101012"/>\n'
    # os quatro grupos de MOSFETs (6 por motor), perto de cada canto
    for gx, gy in ((7.2, 9.5), (L - 15.2, 9.5), (L - 15.2, A - 16.5), (7.2, A - 16.5)):
        for i in range(3):
            for j in range(2):
                c += f'  <rect x="{gx + i * 2.8:.2f}" y="{gy + j * 3.6:.2f}" width="2.3" height="2.8" rx="0.3" fill="#1a1b1e" stroke="#3a3c41" stroke-width="0.15"/>\n'
    # o microcontrolador de cada canal e o capacitor de entrada
    c += f'  <rect x="{L / 2 - 3.2:.2f}" y="{A / 2 - 3.2:.2f}" width="6.4" height="6.4" rx="0.5" fill="#15161a" stroke="#4a4d54" stroke-width="0.2"/>\n'
    c += f'  <rect x="{L / 2 - 1.6:.2f}" y="{A / 2 + 6.0:.2f}" width="3.2" height="1.6" rx="0.3" fill="#c79a3a"/>\n'
    # o enfeite: uma faixa de LED na borda (as 4-em-1 de corrida vem assim)
    c += f'  <rect x="8.0" y="{A - 7.0:.2f}" width="{L - 16.0}" height="0.9" rx="0.45" fill="{letra}" fill-opacity="0.85"/>\n'
    c += texto(L / 2, A / 2 - 7.2, '4IN1', 2.6, letra)
    c += texto(L / 2, A / 2 - 4.6, f'{amperes}A  BLHeli_32', 1.25, letra)
    c += texto(L / 2, A / 2 + 10.6, '3-6S', 1.2, letra)
    # os pinos: motores nas laterais, sinais em cima, bateria embaixo
    for (x, ys, motor) in ((X_ESQ, YS_CIMA, 1), (X_DIR, YS_CIMA, 2), (X_DIR, YS_BAIXO, 3), (X_ESQ, YS_BAIXO, 4)):
        ancora = 'start' if x < L / 2 else 'end'
        dx = 1.4 if x < L / 2 else -1.4
        # o rotulo do lado de dentro do grupo, longe do furo do canto
        y_rotulo = ys[-1] + 2.6 if ys[0] < A / 2 else ys[0] - 1.6
        c += texto(x + dx, y_rotulo, f'M{motor}', 1.1, letra, ancora)
        for y in ys:
            c += f'  <rect x="{x - 1.1:.2f}" y="{y - 0.8:.2f}" width="2.2" height="1.6" rx="0.4" fill="#d9b44a" stroke="#8a6d1f" stroke-width="0.2"/>\n'
    c += f'  <rect x="{XS_CONECTOR[0] - 1.6:.2f}" y="0.2" width="{XS_CONECTOR[-1] - XS_CONECTOR[0] + 3.2:.2f}" height="3.4" rx="0.4" fill="#f2f2ee" stroke="#9a9a94" stroke-width="0.2"/>\n'
    for x, n in zip(XS_CONECTOR, ['S1', 'S2', 'S3', 'S4', 'G']):
        c += ponta(x, Y_CONECTOR)
        c += texto(x, 5.4, n, 0.9, letra)
    for x, n in zip(XS_BAT, ['+', '-']):
        c += f'  <rect x="{x - 2.0:.2f}" y="{Y_BAT - 1.3:.2f}" width="4.0" height="2.6" rx="0.6" fill="#d9b44a" stroke="#8a6d1f" stroke-width="0.25"/>\n'
        c += texto(x, Y_BAT - 2.4, 'BAT' + n, 1.1, letra)
    gravar_svg(nome, L, A, c)


esc_4em1(45, 'preto', 'esc-4em1')
for amperes in AMPERES_4EM1:
    for cor in CORES:
        esc_4em1(amperes, cor, f'esc-4em1-{amperes}a-{cor}')
print(f'esc 4-em-1: {1 + len(AMPERES_4EM1) * len(CORES)} desenhos')

pinos = []
for (x, ys, motor) in ((X_ESQ, YS_CIMA, 1), (X_DIR, YS_CIMA, 2), (X_DIR, YS_BAIXO, 3), (X_ESQ, YS_BAIXO, 4)):
    for y, f in zip(ys, 'ABC'):
        pinos.append((f'M{motor}{f}', x, y, [],
                      f'Motor {motor}, fase {f} (as tres fases nao tem ordem: trocar duas inverte o giro)'))
for x, n in zip(XS_CONECTOR, ['S1', 'S2', 'S3', 'S4', 'GND']):
    pinos.append((n, x, Y_CONECTOR, ['power'] if n == 'GND' else [],
                  'GND do sinal (o mesmo da bateria; ligue no GND da placa)' if n == 'GND'
                  else f'Sinal do motor {n[1]}: pulso de servo (1000 us parado, 2000 us maximo), de um GPIO ou do receptor'))
pinos.append(('BAT+', XS_BAT[0], Y_BAT, ['power'], 'Bateria + (LiPo de 3S a 6S: 11,1 a 22,2 V)'))
pinos.append(('BAT-', XS_BAT[1], Y_BAT, ['power'], 'Bateria - (e o mesmo GND do conector de sinal)'))

cam = BASE + 'app/assets/pecas.json'
d = json.load(io.open(cam, encoding='utf-8'))
d = [p for p in d if p['id'] != 'esc-4em1'] + [{
    'id': 'esc-4em1',
    'largura': round(L * PX),
    'altura': round(A * PX),
    'pinos': [{'nome': n, 'x': round(x * PX, 2), 'y': round(y * PX, 2), 'sinais': s, 'descricao': ds}
              for n, x, y, s, ds in pinos],
    'variantes': [], 'metades': False,
}]
d.sort(key=lambda p: p['id'])
io.open(cam, 'w', encoding='utf-8', newline='').write(json.dumps(d, ensure_ascii=False, indent=1))
print(f'catalogo: {len(d)} pecas (esc-4em1 com {len(pinos)} pinos)')
