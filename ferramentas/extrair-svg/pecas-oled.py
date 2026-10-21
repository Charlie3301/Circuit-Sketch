"""python ferramentas/extrair-svg/pecas-oled.py

Os OLEDs monocromaticos, redesenhados na Alpha 25 no estilo das placas, no
tamanho real (as medidas do lcdwiki e das fichas dos paineis; o lugar dos
furos e dos pinos medido nas fotos de frente):

  - ssd1306-i2c    : o 0,96" de 4 pinos (128 x 64): a placa azul de 27,3 x
                     27,8 mm, os pinos em cima (GND, VCC, SCL, SDA), o vidro
                     quase da largura da placa e a area ativa de 21,744 x
                     10,864 mm; embaixo, o flex dobrado.
  - ssd1306        : o de SPI do Wokwi (o elemento "wokwi-ssd1306", com DATA,
                     CLK, DC, RST, CS, 3V3, VIN e GND): a mesma placa e os
                     mesmos pinos dele, para o diagram.json continuar valendo,
                     no estilo novo e sem marca.
  - ssd1306-128x32 : o 0,91" (128 x 32), novo: a placa de 12 x 38 mm com os
                     4 pinos na ponta (SDA, SCL, VCC, GND) e a area ativa de
                     22,384 x 5,584 mm.
  - sh1106         : o 1,3" (128 x 64, o SH1106), novo: a placa de 33,5 x
                     35,4 mm, os pinos em cima, o vidro de 34,7 mm (passa um
                     pouco da placa) e a area ativa de 29,42 x 14,7 mm.

A cor dos pontos acesos (branco, azul ou amarelo e azul) e o modelo da peca: o
editor pinta por cima. A area ativa sai em fracao da caixa, para o
EditorCircuito.AreasDeTela.
"""
import importlib.util
import io
import json
import os

AQUI = os.path.dirname(os.path.abspath(__file__))
_spec = importlib.util.spec_from_file_location('placas_arduino', os.path.join(AQUI, 'placas-arduino.py'))
pa = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(pa)

Desenho, T, f = pa.Desenho, pa.T, pa.f
ESTANHO = pa.ESTANHO
A = 96 / 25.4
PASSO = 2.54
AZUL, AZUL_BORDA, BRANCO = '#1d4fa6', '#0f2c66', '#f3f3f1'

pecas = []


def svg_de(d, L, H, sobra_x=0.0):
    w, h = L * A + 2 * sobra_x, H * A
    extra = f' data-sobra="{f(sobra_x)} 0"' if sobra_x else ''
    return (f'<svg xmlns="http://www.w3.org/2000/svg" version="1.1" width="{f(w)}px" height="{f(h)}px"{extra} '
            f'viewBox="0 0 {f(w)} {f(h)}">\n<defs>\n{pa.DEFS}</defs>\n' + ''.join(d.partes) + '</svg>\n')


def gravar(nome, svg):
    io.open(pa.BASE + f'app/assets/{nome}.svg', 'w', encoding='utf-8', newline='').write(svg)


def guardar(id_peca, L, H, pinos, area):
    pecas.append({'id': id_peca, 'largura': round(L * A), 'altura': round(H * A),
                  'pinos': [{'nome': n, 'x': round(x * A, 2), 'y': round(y * A, 2), 'sinais': s, 'descricao': dsc}
                            for n, x, y, s, dsc in pinos],
                  'variantes': [], 'metades': False})
    ax0, ay0, ax1, ay1 = area
    print(f'{id_peca}: {round(L * A)}x{round(H * A)} px, {len(pinos)} pinos; '
          f'area ({ax0 / L:.4f}, {ay0 / H:.4f}, {(ax1 - ax0) / L:.4f}, {(ay1 - ay0) / H:.4f})')


def placa(d, L, H, rx=0.6):
    d.ret(0, 0, L, H, AZUL, rx=rx, stroke=AZUL_BORDA, sw=0.15)
    d.ret(0, 0, L, H, 'url(#luz)', rx=rx)


def furo(d, x, y, furo_d=1.9, anel_d=2.8):
    d.circ(x, y, anel_d / 2, ESTANHO)
    d.circ(x, y, furo_d / 2, '#0d1a2e')


def pino_soldado(d, x, y, quadrado=False):
    if quadrado:
        d.ret_c(x, y, 1.7, 1.7, ESTANHO)
    else:
        d.circ(x, y, 0.85, ESTANHO)
    d.circ(x, y, 0.55, '#dfe3e7')
    d.circ(x - 0.15, y - 0.15, 0.2, '#ffffff', op=0.7)


def vidro_oled(d, x0, y0, x1, y1, area, base_y, flex=None):
    """O painel: o vidro escuro, a area ativa (preta, apagada), a faixa do chip
    embaixo com as trilhas douradas, e o flex dobrado para tras da placa."""
    ax0, ay0, ax1, ay1 = area
    if flex:                                                      # o flex, antes do vidro (sai de baixo dele)
        fx0, fx1, fy1 = flex
        d.ret(fx0, base_y - 1.0, fx1 - fx0, fy1 - base_y + 1.0, '#1a1410', rx=0.4)
        d.ret(fx0 + 0.4, fy1 - 0.9, fx1 - fx0 - 0.8, 0.6, '#c98a2e', rx=0.2)
        for k in range(int((fx1 - fx0 - 1.5) / 0.6)):
            d.ret(fx0 + 0.8 + k * 0.6, base_y, 0.18, fy1 - base_y - 1.0, '#3a2c1c')
    d.ret(x0, y0, x1 - x0, y1 - y0, '#16181d', rx=0.25, stroke='#2b2e35', sw=0.1)
    d.ret(x0 + 0.3, y0 + 0.3, x1 - x0 - 0.6, base_y - y0 - 0.6, '#0b0c10', rx=0.2)
    d.ret(ax0, ay0, ax1 - ax0, ay1 - ay0, '#050608')
    d.ret(x0 + 0.3, base_y - 0.3, x1 - x0 - 0.6, y1 - base_y, '#1d1f24', rx=0.2)          # a faixa do chip
    d.ret((x0 + x1) / 2 - (x1 - x0) * 0.22, base_y + 0.3, (x1 - x0) * 0.44, (y1 - base_y) * 0.55, '#0e0f12')
    for lado in (-1, 1):                                                                  # as trilhas douradas
        xa = (x0 + x1) / 2 + lado * (x1 - x0) * 0.24
        xb = (x0 + x1) / 2 + lado * (x1 - x0) * 0.42
        d.linha(xa, base_y + 0.5, xb, base_y + 0.5, '#b88a3a', 0.15)
        d.linha(xb, base_y + 0.5, xb, y1 - 0.6, '#b88a3a', 0.15)
    d.ret(x0 + 0.3, y0 + 0.3, x1 - x0 - 0.6, (base_y - y0) * 0.22, '#ffffff', op=0.04)      # o reflexo


I2C = [('GND', ['power'], 'GND'), ('VCC', ['power'], 'Alimentacao: 3,3 a 5 V (o modulo tem regulador)'),
       ('SCL', [], 'SCL do I2C (o D22 na ESP32, o A5 no Uno)'), ('SDA', [], 'SDA do I2C (o D21 na ESP32, o A4 no Uno)')]


# =====================================================================
#  0,96" I2C (4 pinos)
# =====================================================================
L, H = 27.3, 27.8
AREA = (L / 2 - 21.744 / 2, 6.0, L / 2 + 21.744 / 2, 6.0 + 10.864)
XS = [9.84 + k * PASSO for k in range(4)]
Y_PINO = 1.6
d = Desenho(T(A, 0, 0, A, 0, 0))
placa(d, L, H)
for x in (2.0, L - 2.0):
    for y in (2.0, H - 2.0):
        furo(d, x, y, 1.7, 2.5)
for k, x in enumerate(XS):
    pino_soldado(d, x, Y_PINO, quadrado=k == 0)
    d.texto(x, Y_PINO + 1.75, I2C[k][0], 0.7, BRANCO, alinhar='center')
vidro_oled(d, 0.3, 4.3, L - 0.3, 23.1, AREA, 18.6, flex=(7.3, 19.9, 26.6))
gravar('ssd1306-i2c', svg_de(d, L, H))
guardar('ssd1306-i2c', L, H, [(n, XS[k], Y_PINO, s, dsc) for k, (n, s, dsc) in enumerate(I2C)], AREA)


# =====================================================================
#  0,96" SPI do Wokwi (8 pinos, a geometria e os pinos dele)
# =====================================================================
L, H = 39.69, 30.69
NOMES_SPI = ['DATA', 'CLK', 'DC', 'RST', 'CS', '3V3', 'VIN', 'GND']
DESC_SPI = {'DATA': 'Dados do SPI (o MOSI da placa)', 'CLK': 'O relogio do SPI (o SCK da placa)',
            'DC': 'Dado/comando: LOW e comando, HIGH e dado', 'RST': 'Reset (LOW reinicia o SSD1306)',
            'CS': 'Chip select do SPI (LOW: o display escuta)', '3V3': 'Saida de 3,3 V do regulador do modulo',
            'VIN': 'Alimentacao: 3 a 5 V', 'GND': 'GND'}
XS_SPI = [9.66, 12.04, 14.42, 17.07, 19.71, 22.09, 24.74, 27.38]
Y_SPI = 3.31
AREA_SPI = (0.076 * L, 0.2241 * H, (0.076 + 0.8533) * L, (0.2241 + 0.5517) * H)
d = Desenho(T(A, 0, 0, A, 0, 0))
placa(d, L, H, rx=3.0)
for x in (3.7, L - 3.7):
    for y in (3.7, H - 3.7):
        furo(d, x, y, 2.6, 3.6)
for k, (x, n) in enumerate(zip(XS_SPI, NOMES_SPI)):
    pino_soldado(d, x, Y_SPI, quadrado=k == 0)
    # as legendas alternadas, em cima e embaixo (as de baixo menores, antes do vidro)
    if k % 2 == 0:
        d.texto(x, Y_SPI - 1.95, n, 0.7, BRANCO, alinhar='center')
    else:
        d.texto(x, Y_SPI + 1.45, n, 0.55, BRANCO, alinhar='center')
ax0, ay0, ax1, ay1 = AREA_SPI
vidro_oled(d, ax0 - 1.6, ay0 - 1.4, ax1 + 1.6, ay1 + 5.5, AREA_SPI, ay1 + 1.6, flex=(13.5, 26.2, ay1 + 7.6))
gravar('ssd1306', svg_de(d, L, H))
guardar('ssd1306', L, H, [(n, XS_SPI[k], Y_SPI if n != 'GND' else 3.18, ['power'] if n in ('VIN', 'GND', '3V3') else [], DESC_SPI[n])
                          for k, n in enumerate(NOMES_SPI)], AREA_SPI)


# =====================================================================
#  0,91" (128 x 32), a placa de 12 x 38 mm
# =====================================================================
L, H = 144 / A, 45 / A                                # 38,1 x 11,9 mm: a caixa inteira (144 x 45 px)
AREA_91 = (7.0, H / 2 - 5.584 / 2, 7.0 + 22.384, H / 2 + 5.584 / 2)
YS_91 = [H / 2 + (k - 1.5) * PASSO for k in range(4)]                  # de cima para baixo: SDA, SCL, VCC, GND
NOMES_91 = ['SDA', 'SCL', 'VCC', 'GND']
X_91 = 1.6
d = Desenho(T(A, 0, 0, A, 0, 0))
placa(d, L, H, rx=0.5)
for k, y in enumerate(YS_91):
    pino_soldado(d, X_91, y, quadrado=k == 3)
    d.texto(X_91 + 1.55, y, NOMES_91[k], 0.62, BRANCO, alinhar='center-left')
d.ret(34.4, 2.4, 3.2, 7.2, '#1a1410', rx=0.4)                                         # o flex dobrado na ponta
d.ret(37.0, 2.8, 0.6, 6.4, '#c98a2e', rx=0.2)
d.ret(5.0, 0.6, 30.0, 10.8, '#16181d', rx=0.25, stroke='#2b2e35', sw=0.1)            # o vidro
d.ret(5.3, 0.9, 25.4, 10.2, '#0b0c10', rx=0.2)
d.ret(*AREA_91[:2], AREA_91[2] - AREA_91[0], AREA_91[3] - AREA_91[1], '#050608')
d.ret(30.7, 0.9, 4.0, 10.2, '#1d1f24', rx=0.2)                                        # a faixa do chip
d.ret(31.4, 3.2, 2.0, 5.6, '#0e0f12')
d.ret(5.3, 0.9, 25.4, 2.0, '#ffffff', op=0.04)
gravar('ssd1306-128x32', svg_de(d, L, H))
dsc_91 = {n: dsc for n, _, dsc in I2C}
guardar('ssd1306-128x32', L, H, [(n, X_91, YS_91[k], ['power'] if n in ('VCC', 'GND') else [], dsc_91[n])
                                 for k, n in enumerate(NOMES_91)], AREA_91)


# =====================================================================
#  1,3" SH1106 (128 x 64)
# =====================================================================
L, H = 33.5, 35.4
SOBRA = 0.6                                                    # o vidro de 34,7 mm passa da placa
AREA_13 = (L / 2 - 29.42 / 2, 7.8, L / 2 + 29.42 / 2, 7.8 + 14.7)
XS_13 = [L / 2 + (k - 1.5) * PASSO for k in range(4)]
Y_13 = 2.0
d = Desenho(T(A, 0, 0, A, SOBRA * A, 0))
placa(d, L, H)
for x in (2.4, L - 2.4):
    for y in (2.2, H - 2.8):
        furo(d, x, y, 2.4, 3.4)
for k, x in enumerate(XS_13):
    pino_soldado(d, x, Y_13, quadrado=k == 0)
    d.texto(x, Y_13 + 1.9, I2C[k][0], 0.7, BRANCO, alinhar='center')
vidro_oled(d, -SOBRA, 5.4, L + SOBRA, 30.0, AREA_13, 25.2, flex=(11.3, 22.2, 32.6))
gravar('sh1106', svg_de(d, L, H, SOBRA * A))
guardar('sh1106', L, H, [(n, XS_13[k], Y_13, s, dsc) for k, (n, s, dsc) in enumerate(I2C)], AREA_13)


cam = pa.BASE + 'app/assets/pecas.json'
lista = json.load(io.open(cam, encoding='utf-8'))
novos = {p['id'] for p in pecas}
lista = [p for p in lista if p['id'] not in novos] + pecas
lista.sort(key=lambda p: p['id'])
io.open(cam, 'w', encoding='utf-8', newline='').write(json.dumps(lista, ensure_ascii=False, indent=1))
print(f'catalogo: {len(lista)} pecas')
