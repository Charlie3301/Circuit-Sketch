"""python ferramentas/extrair-svg/pecas-tft.py

Os displays TFT coloridos, redesenhados na Alpha 25 pelos desenhos de contorno
dos fabricantes (os do lcdwiki), no tamanho real e vistos de frente:

  - ili9341 : o modulo vermelho de 2,2" (240 x 320, o MSP2202): a placa de
              40,1 x 67,2 mm, os furos de 3,2 mm a 34,1 x 61,2 mm, o vidro que
              ocupa a largura toda (40,1 x 55,2 mm) com a area ativa de 33,84 x
              45,12 mm, e a barra de 9 pinos embaixo com o pino 1 (VCC) a 9,89 mm
              da esquerda: VCC, GND, CS, RESET, DC, MOSI, SCK, LED, MISO. Em
              cima, os furos do cartao SD (sem a barra, como o modulo vem).

  - st7789  : o IPS de 1,54" (240 x 240, o MSP1541), azul: a placa de 32,00 x
              43,72 mm, os 8 pinos em cima (GND, VCC, SCL, SDA, RES, DC, CS,
              BLK) e o vidro de 31,52 x 35,10 mm, a area ativa de 27,72 mm.
  - st7735  : o de 1,8" (128 x 160, o MSP1803), vermelho: a placa de 34,5 x
              58 mm, a barra de 8 pinos embaixo (VCC, GND, CS, RESET, A0, SDA,
              SCK, LED) e os furos do SD em cima, sem a barra.

O acabamento e o das fotos de frente (telas_a25.py): os furos de fixacao
vazados com o anel de estanho, a solda brilhante de cada pino, a sombra do LCD
(ele fica ~2 mm acima da placa) e a borda do vidro. O ST7789 tem o azul
petroleo da placa da foto e o nome de cada pino embaixo da barra.

A area ativa (onde o editor desenha a imagem do sketch) sai em fracao da caixa,
para o EditorCircuito.AreasDeTela.
"""
import importlib.util
import io
import json
import os

AQUI = os.path.dirname(os.path.abspath(__file__))
_spec = importlib.util.spec_from_file_location('placas_arduino', os.path.join(AQUI, 'placas-arduino.py'))
pa = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(pa)

_spec_t = importlib.util.spec_from_file_location('telas_a25', os.path.join(AQUI, 'telas_a25.py'))
TL = importlib.util.module_from_spec(_spec_t)
_spec_t.loader.exec_module(TL)

Desenho, T, f = pa.Desenho, pa.T, pa.f
ESTANHO = pa.ESTANHO
A = 96 / 25.4
PASSO = 2.54
BRANCO = '#f3f3f1'

DEFS_EXTRA = '''  <linearGradient id="vidro-tft" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#1a1c20"/><stop offset="0.55" stop-color="#0c0d10"/><stop offset="1" stop-color="#16181c"/></linearGradient>
'''

pecas = []


def svg_de(d, L, H):
    w, h = L * A, H * A
    return (f'<svg xmlns="http://www.w3.org/2000/svg" version="1.1" width="{f(w)}px" height="{f(h)}px" '
            f'viewBox="0 0 {f(w)} {f(h)}">\n<defs>\n{pa.DEFS}{TL.DEFS}{DEFS_EXTRA}</defs>\n' + ''.join(d.partes) + '</svg>\n')


def guardar(id_peca, L, H, pinos, area):
    pecas.append({'id': id_peca, 'largura': round(L * A), 'altura': round(H * A),
                  'pinos': [{'nome': n, 'x': round(x * A, 2), 'y': round(y * A, 2), 'sinais': s, 'descricao': dsc}
                            for n, x, y, s, dsc in pinos],
                  'variantes': [], 'metades': False})
    ax0, ay0, ax1, ay1 = area
    print(f'{id_peca}: {round(L * A)}x{round(H * A)} px, {len(pinos)} pinos; '
          f'area ({ax0 / L:.4f}, {ay0 / H:.4f}, {(ax1 - ax0) / L:.4f}, {(ay1 - ay0) / H:.4f})')


def furo(d, x, y, furo_d, anel_d):
    d.circ(x, y, anel_d / 2, ESTANHO)
    d.circ(x, y, furo_d / 2, '#16120f')


def pino_soldado(d, x, y, quadrado=False):
    """O furo da barra com o pino soldado (a ponta para tras, para a protoboard)."""
    if quadrado:
        d.ret_c(x, y, 1.7, 1.7, ESTANHO)
    else:
        d.circ(x, y, 0.85, ESTANHO)
    d.circ(x, y, 0.55, '#dfe3e7')
    d.circ(x - 0.15, y - 0.15, 0.2, '#ffffff', op=0.7)


def tela_tft(d, x0, y0, x1, y1, ax0, ay0, ax1, ay1, fpc_y, flex=None):
    """O LCD montado: a moldura branca da luz de fundo, o vidro e a area ativa
    (preta, apagada), e o flex do vidro embaixo -- o azul da largura toda, ou
    (com flex=(x0, x1)) a faixa azul fina do chip e a tira laranja so no meio."""
    TL.sombra_do_painel(d, x0, y0, x1, y1, rx=0.4, alto=2.0, op=0.4)
    d.ret(x0, y0, x1 - x0, y1 - y0, '#e9e9e6', rx=0.4, stroke='#b9b9b4', sw=0.1)
    d.ret(x0 + 0.5, y0 + 0.5, x1 - x0 - 1.0, fpc_y - y0 - 0.5, '#2a2c30', rx=0.3)          # o vidro
    d.ret(ax0, ay0, ax1 - ax0, ay1 - ay0, 'url(#vidro-tft)')
    d.ret(ax0 - 0.25, ay0 - 0.25, ax1 - ax0 + 0.5, ay1 - ay0 + 0.5, None, stroke='#3a3d42', sw=0.12)
    d.ret(x0 + 0.5, y0 + 0.5, x1 - x0 - 1.0, fpc_y - y0 - 0.5, None, rx=0.3, stroke='url(#vidro-borda)', sw=0.15)
    if flex:
        fx0, fx1 = flex
        d.ret(x0 + 0.5, fpc_y, x1 - x0 - 1.0, 1.2, '#1f5fb4', rx=0.2)                        # a borda do vidro, com o chip
        d.ret(fx0, fpc_y + 1.2, fx1 - fx0, y1 - fpc_y - 1.6, '#c98a2e', rx=0.2)              # o flex laranja
        for k in range(int((fx1 - fx0 - 2) / 0.6)):
            d.ret(fx0 + 1.0 + k * 0.6, fpc_y + 1.5, 0.25, y1 - fpc_y - 2.4, '#e8b35a', op=0.6)
    else:
        d.ret(x0 + 0.5, fpc_y, x1 - x0 - 1.0, y1 - fpc_y - 0.4, '#1f5fb4', rx=0.2)              # o flex
        for k in range(int((x1 - x0 - 6) / 0.5)):
            d.ret(x0 + 3 + k * 0.5, fpc_y + 0.5, 0.25, y1 - fpc_y - 1.4, '#d7b350', op=0.55)
    d.ret(x0 + 0.5, y0 + 0.5, x1 - x0 - 1.0, (fpc_y - y0) * 0.2, '#ffffff', op=0.05)         # o reflexo


# =====================================================================
#  ILI9341 de 2,2" (MSP2202)
# =====================================================================
L, H = 40.10, 67.20
PINOS = [('VCC', ['power'], 'Alimentacao: 3,3 a 5 V (o modulo tem regulador)'),
         ('GND', ['power'], 'GND'),
         ('CS', [], 'Chip select do SPI (LOW: o display escuta)'),
         ('RST', [], 'Reset (LOW reinicia o ILI9341)'),
         ('D/C', [], 'Dado/comando: LOW e comando, HIGH e dado'),
         ('MOSI', [], 'MOSI (SDI): os dados do SPI'),
         ('SCK', [], 'O relogio do SPI'),
         ('LED', [], 'Luz de fundo: no 3,3 V acesa; num pino, HIGH acende e o PWM faz a meia luz'),
         ('MISO', [], 'MISO (SDO): a leitura do display (pode ficar solto)')]
X1, Y_BARRA = 9.89, H - 1.50
AREA = (3.13, 8.40, 3.13 + 33.84, 8.40 + 45.12)
d = Desenho(T(A, 0, 0, A, 0, 0))
TL.placa(d, 0, 0, L, H, 0.8, '#d42a22', '#7c140f', [(x, y, 3.2, 4.7) for x in (3.0, L - 3.0) for y in (3.0, H - 3.0)])
for k in range(5):                                                   # os furos do cartao SD (sem a barra)
    furo(d, 14.97 + k * PASSO, 2.18, 1.0, 1.7)
tela_tft(d, 0.0, 5.40, L, 60.60, *AREA, fpc_y=57.6)
for k, (n, _, _) in enumerate(PINOS):
    TL.solda(d, X1 + k * PASSO, Y_BARRA, quadrado=k == 0)
d.texto(X1 - 1.8, Y_BARRA, '1', 0.9, BRANCO, alinhar='center')
d.texto(X1 + 8 * PASSO + 1.8, Y_BARRA, '9', 0.9, BRANCO, alinhar='center')
io.open(pa.BASE + 'app/assets/ili9341.svg', 'w', encoding='utf-8', newline='').write(svg_de(d, L, H))
guardar('ili9341', L, H, [(n, X1 + k * PASSO, Y_BARRA, s, dsc) for k, (n, s, dsc) in enumerate(PINOS)], AREA)



# =====================================================================
#  ST7789 de 1,54" (240 x 240, o MSP1541)
# =====================================================================
# A placa de 32,00 x 43,72 mm e o vidro (a luz de fundo de 31,52 x 35,10 mm, a
# area ativa de 27,72 x 27,72 mm a 2,14 mm da borda) sao do manual e da ficha
# do painel (TFT1541); o lugar dos furos e dos pinos, medido na foto.
L, H = 32.00, 43.72
PINOS_89 = [('GND', ['power'], 'GND'), ('VCC', ['power'], 'Alimentacao: 3,3 V'),
            ('SCL', [], 'O relogio do SPI (o SCK da placa)'), ('SDA', [], 'Os dados do SPI (o MOSI da placa)'),
            ('RES', [], 'Reset (LOW reinicia o ST7789)'), ('DC', [], 'Dado/comando: LOW e comando, HIGH e dado'),
            ('CS', [], 'Chip select do SPI (LOW: o display escuta)'),
            ('BLK', [], 'Luz de fundo: solto ou no 3,3 V acesa; num pino, HIGH acende e o PWM faz a meia luz')]
Y_89 = 2.0
XS_89 = [L / 2 + (k - 3.5) * PASSO for k in range(8)]
BL_X0, BL_Y0 = (L - 31.52) / 2, 4.0
AREA_89 = (BL_X0 + 1.90, BL_Y0 + 2.14, BL_X0 + 1.90 + 27.72, BL_Y0 + 2.14 + 27.72)
d = Desenho(T(A, 0, 0, A, 0, 0))
TL.placa(d, 0, 0, L, H, 0.8, '#1c6e9e', '#0f4466', [(x, y, 2.2, 3.6) for x in (2.2, L - 2.2) for y in (2.2, H - 2.2)])
tela_tft(d, BL_X0, BL_Y0, BL_X0 + 31.52, BL_Y0 + 35.10, *AREA_89, fpc_y=BL_Y0 + 32.3)
for k, x in enumerate(XS_89):
    TL.solda(d, x, Y_89, quadrado=k == 0)
    d.texto(x, Y_89 + 1.3, PINOS_89[k][0], 0.62, BRANCO, alinhar='center', peso='normal')
d.texto(L / 2, H - 1.7, "1.54'IPS  240*240(RGB)", 1.05, BRANCO, alinhar='center', peso='bold')
io.open(pa.BASE + 'app/assets/st7789.svg', 'w', encoding='utf-8', newline='').write(svg_de(d, L, H))
guardar('st7789', L, H, [(n, XS_89[k], Y_89, s, dsc) for k, (n, s, dsc) in enumerate(PINOS_89)], AREA_89)


# =====================================================================
#  ST7735 de 1,8" (128 x 160, o MSP1803)
# =====================================================================
# A placa de 34,5 x 58,0 mm e a area ativa de 28,03 x 35,04 mm sao do manual;
# os furos, as barras e o vidro, medidos nas fotos de frente e de verso.
L, H = 34.5, 58.0
PINOS_35 = [('VCC', ['power'], 'Alimentacao: 3,3 a 5 V (o modulo tem regulador)'), ('GND', ['power'], 'GND'),
            ('CS', [], 'Chip select do SPI (LOW: o display escuta)'), ('RESET', [], 'Reset (LOW reinicia o ST7735)'),
            ('A0', [], 'Dado/comando (o DC): LOW e comando, HIGH e dado'), ('SDA', [], 'Os dados do SPI (o MOSI da placa)'),
            ('SCK', [], 'O relogio do SPI'),
            ('LED', [], 'Luz de fundo: no 3,3 V acesa; num pino, HIGH acende e o PWM faz a meia luz')]
Y_35 = H - 2.36
XS_35 = [8.7 + k * PASSO for k in range(8)]
AREA_35 = ((L - 28.03) / 2, 8.2, (L + 28.03) / 2, 8.2 + 35.04)
d = Desenho(T(A, 0, 0, A, 0, 0))
TL.placa(d, 0, 0, L, H, 0.8, '#d42a22', '#7c140f', [(x, y, 2.8, 4.0) for x in (3.0, L - 3.0) for y in (3.0, H - 3.0)])
for k in range(4):                                                    # os furos do cartao SD (sem a barra)
    furo(d, 13.7 + k * PASSO, 2.4, 1.0, 1.7)
d.texto(13.7 + 3 * PASSO + 2.3, 2.4, 'J4', 0.9, BRANCO, alinhar='center-left')
tela_tft(d, 0.25, 5.2, L - 0.25, 53.0, *AREA_35, fpc_y=46.6, flex=(9.5, 25.0))
for k, x in enumerate(XS_35):
    TL.solda(d, x, Y_35, quadrado=k == 0)
d.texto(XS_35[0] - 2.3, Y_35, 'J2', 0.9, BRANCO, alinhar='center-right')
io.open(pa.BASE + 'app/assets/st7735.svg', 'w', encoding='utf-8', newline='').write(svg_de(d, L, H))
guardar('st7735', L, H, [(n, XS_35[k], Y_35, s, dsc) for k, (n, s, dsc) in enumerate(PINOS_35)], AREA_35)


cam = pa.BASE + 'app/assets/pecas.json'
lista = json.load(io.open(cam, encoding='utf-8'))
novos = {p['id'] for p in pecas}
lista = [p for p in lista if p['id'] not in novos] + pecas
lista.sort(key=lambda p: p['id'])
io.open(cam, 'w', encoding='utf-8', newline='').write(json.dumps(lista, ensure_ascii=False, indent=1))
print(f'catalogo: {len(lista)} pecas')
