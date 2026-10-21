"""python ferramentas/extrair-svg/modulo-i2c.py

O modulo I2C do LCD (o adaptador FC-113, com o PCF8574T), redesenhado na
Alpha 25 na escala real: 41,5 x 19 mm, visto do lado dos componentes, como
ele fica soldado atras do display. A barra de 16 pinos corre pela borda de
cima com o pino 1 (VSS) na direita, ao lado do conector de 4 pinos (GND, VCC,
SDA, SCL); na outra ponta, o jumper do LED (a luz de fundo do LCD) e o LED de
energia. No meio, o PCF8574T, o trimpot azul do contraste e os jumpers de
endereco A0, A1 e A2 (abertos: o endereco 0x27).

O lugar de cada peca foi medido na foto do modulo do guia da Handson
Technology (I2C_1602_LCD.pdf). O desenho e nosso.
"""
import importlib.util
import io
import json
import os

AQUI = os.path.dirname(os.path.abspath(__file__))
_spec = importlib.util.spec_from_file_location('placas_arduino', os.path.join(AQUI, 'placas-arduino.py'))
pa = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(pa)

Desenho, T, f, rodar, mover = pa.Desenho, pa.T, pa.f, pa.rodar, pa.mover
SERIGRAFIA, ESTANHO, OURO, PRETO_CI, MARCA_CI = pa.SERIGRAFIA, pa.ESTANHO, pa.OURO, pa.PRETO_CI, pa.MARCA_CI
A = 96 / 25.4
L, H = 41.5, 19.0
MASCARA, BORDA = '#18191b', '#060607'

LCD16 = ['VSS', 'VDD', 'V0', 'RS', 'RW', 'E', 'D0', 'D1', 'D2', 'D3', 'D4', 'D5', 'D6', 'D7', 'A', 'K']
I2C4 = ['GND', 'VCC', 'SDA', 'SCL']
X_PINO1, Y_BARRA = 39.5, 1.5            # o pino 1 da barra do LCD (os outros para a esquerda)
X_I2C, Y0_I2C = 40.0, 5.9                # o conector de 4 pinos, de cima para baixo
LED = (5.9, 15.4)


def dois(d, cx, cy, vertical, corpo, comp=1.6, larg=0.85):
    w, h = (larg, comp) if vertical else (comp, larg)
    d.ret_c(cx, cy, w + 0.55, h + 0.55, None, rx=0.12, stroke=SERIGRAFIA, sw=0.12)
    d.ret_c(cx, cy, w, h, corpo, rx=0.06)
    d.ret_c(cx, cy, w, h, 'url(#luz)', rx=0.06)
    t = comp * 0.22
    if vertical:
        d.ret(cx - w / 2, cy - h / 2, w, t, 'url(#metal)')
        d.ret(cx - w / 2, cy + h / 2 - t, w, t, 'url(#metal)')
    else:
        d.ret(cx - w / 2, cy - h / 2, t, h, 'url(#metal)')
        d.ret(cx + w / 2 - t, cy - h / 2, t, h, 'url(#metal)')


def gerar():
    d = Desenho(T(A, 0, 0, A, 0, 0))
    d.ret(0, 0, L, H, MASCARA, rx=0.6, stroke=BORDA, sw=0.12)
    d.ret(0, 0, L, H, 'url(#luz)', rx=0.6)

    # ---- a barra do LCD (16) e o conector I2C (4): ilhas com o pino soldado ----
    for k in range(16):
        x = X_PINO1 - k * 2.54
        if k == 0:
            d.ret_c(x, Y_BARRA, 1.6, 1.6, OURO, rx=0.1)
        else:
            d.circ(x, Y_BARRA, 0.8, OURO)
        d.circ(x, Y_BARRA, 0.66, 'url(#lata)', stroke='#7c838b', sw=0.05)
        d.ret_c(x, Y_BARRA, 0.6, 0.6, '#9aa1a8', rx=0.1)
    d.texto(X_PINO1 - 15 * 2.54, 3.5, '16', 0.95, SERIGRAFIA, ancora='middle', alinhar='center', peso='normal')
    d.texto(X_PINO1, 3.5, '1', 0.95, SERIGRAFIA, ancora='middle', alinhar='center', peso='normal')
    d.ret(X_I2C - 1.35, Y0_I2C - 1.35, 2.7, 3 * 2.54 + 2.7, 'url(#plastico)', rx=0.2)
    for k, nome in enumerate(I2C4):
        y = Y0_I2C + k * 2.54
        d.ret_c(X_I2C, y, 1.6, 1.6, '#202226', rx=0.35)
        d.ret_c(X_I2C, y, 0.84, 0.84, '#7a5c18')
        d.ret_c(X_I2C, y, 0.64, 0.64, 'url(#ouro)')
        d.texto(X_I2C - 1.9, y, nome, 0.95, SERIGRAFIA, ancora='end', alinhar='center-right', peso='normal')

    # ---- o jumper do LED (a luz de fundo) e o LED de energia ----
    # (deitado: os dois pinos lado a lado, o capacete preto por cima)
    d.ret(2.06, 8.13, 5.08, 2.54, 'url(#plastico)', rx=0.2)
    d.ret(2.3, 7.75, 4.6, 3.3, '#141517', rx=0.4, stroke='#000', sw=0.06)
    d.ret(2.65, 8.1, 3.9, 2.6, None, rx=0.3, stroke='#3c3f45', sw=0.08)
    d.texto(3.0, 6.7, 'LED', 0.9, SERIGRAFIA, ancora='middle', alinhar='center', peso='normal')
    d.ret_c(LED[0], LED[1], 1.6, 0.85, '#f4f2ec', rx=0.08)
    d.ret_c(LED[0], LED[1], 0.9, 0.55, '#e3dfe6', rx=0.1)
    d.ret(LED[0] - 0.8, LED[1] - 0.425, 0.35, 0.85, 'url(#metal)')
    d.ret(LED[0] + 0.45, LED[1] - 0.425, 0.35, 0.85, 'url(#metal)')
    d.texto(5.9, 17.6, 'POWER', 0.85, SERIGRAFIA, ancora='middle', alinhar='center', peso='normal')

    # ---- os resistores e capacitores entre o jumper e o chip; o transistor da luz ----
    for x, y, vertical, corpo in ((8.2, 9.2, True, '#1e1f22'), (9.8, 9.2, True, '#1e1f22'), (11.4, 9.2, True, '#1e1f22'),
                                  (8.9, 13.6, False, '#b7956a'), (12.3, 13.6, False, '#1e1f22'),
                                  (10.2, 16.6, False, '#1e1f22')):
        dois(d, x, y, vertical, corpo)
    for x in (12.0, 13.9):
        d.ret_c(x, 5.7, 0.45, 0.7, 'url(#metal-h)')
    d.ret_c(12.95, 7.1, 0.45, 0.7, 'url(#metal-h)')
    d.ret_c(12.95, 6.4, 2.9, 1.3, PRETO_CI, rx=0.1)
    d.texto(12.95, 6.4, 'J3Y', 0.5, MARCA_CI, ancora='middle', alinhar='center', peso='normal')

    # ---- o PCF8574T (SO-16), de pe, com as pernas dos lados ----
    cx, cy = 21.3, 11.0
    for k in range(8):
        y = cy + (k - 3.5) * 1.27
        d.ret_c(cx - 3.75, y, 1.3, 0.42, 'url(#metal)')
        d.ret_c(cx + 3.75, y, 1.3, 0.42, 'url(#metal)')
    d.entrar(mover(cx, cy) * rodar(90))
    pa.ci(d, 10.3, 6.2, [('0943R', 0.6), ('PCF8574T', 0.85)], pino1=(-4.4, -2.3))
    d.sair()

    # ---- o trimpot do contraste (azul, com o rotor branco) ----
    d.ret_c(30.6, 6.9, 6.2, 6.6, '#2f6fd0', rx=0.4, stroke='#1d4f9c', sw=0.1)
    d.ret_c(30.6, 6.9, 6.2, 6.6, 'url(#luz)', rx=0.4)
    d.circ(30.6, 6.6, 2.1, '#f1f1ee', stroke='#9a9a94', sw=0.1)
    d.ret_c(30.6, 6.6, 0.55, 3.0, '#8d8f93', rx=0.2)
    d.ret_c(30.6, 6.6, 3.0, 0.55, '#8d8f93', rx=0.2)

    # ---- os jumpers de endereco (abertos de fabrica) ----
    for k, nome in enumerate(('A0', 'A1', 'A2')):
        x = 29.4 + k * 2.1
        d.ret_c(x, 12.6, 0.9, 0.8, ESTANHO, rx=0.1)
        d.ret_c(x, 13.8, 0.9, 0.8, ESTANHO, rx=0.1)
        d.texto(x, 15.4, nome, 0.85, SERIGRAFIA, ancora='middle', alinhar='center', peso='normal')
    dois(d, 36.2, 17.4, False, '#1e1f22')
    dois(d, 36.2, 15.6, False, '#b7956a')

    svg = (f'<svg xmlns="http://www.w3.org/2000/svg" version="1.1" width="{f(L * A)}px" height="{f(H * A)}px" '
           f'viewBox="0 0 {f(L * A)} {f(H * A)}">\n<defs>\n{pa.DEFS}</defs>\n' + ''.join(d.partes) + '</svg>\n')
    io.open(pa.BASE + 'app/assets/modulo-i2c.svg', 'w', encoding='utf-8', newline='').write(svg)

    cam = pa.BASE + 'app/assets/pecas.json'
    lista = json.load(io.open(cam, encoding='utf-8'))
    peca = next(p for p in lista if p['id'] == 'modulo-i2c')
    antigos = {q['nome']: q for q in peca['pinos']}
    novos = []
    for k, nome in enumerate(LCD16):
        q = dict(antigos[nome]); q['x'], q['y'] = round((X_PINO1 - k * 2.54) * A, 2), round(Y_BARRA * A, 2)
        novos.append(q)
    for k, nome in enumerate(I2C4):
        q = dict(antigos[nome]); q['x'], q['y'] = round(X_I2C * A, 2), round((Y0_I2C + k * 2.54) * A, 2)
        novos.append(q)
    peca['largura'], peca['altura'], peca['pinos'] = round(L * A), round(H * A), novos
    io.open(cam, 'w', encoding='utf-8', newline='').write(json.dumps(lista, ensure_ascii=False, indent=1))
    print(f'modulo-i2c: {round(L * A)}x{round(H * A)} px, {len(novos)} pinos; LED em ({LED[0] * A:.1f}, {LED[1] * A:.1f}) px')


if __name__ == '__main__':
    gerar()
