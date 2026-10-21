"""python ferramentas/extrair-svg/placas-stm32.py [placa ...]

As tres placas STM32 redesenhadas (Alpha 25), no estilo das outras placas (as
primitivas e a paleta de placas-arduino.py), na escala real (96 px por
polegada). O desenho de cada peca e nosso; o lugar e o tamanho de cada uma
sairam de:

  - Nucleo-L031K6 (MB1180, Nucleo-32): o desenho mecanico vetorial do manual
    UM1956 da ST (figura 5): nucleo32_layout.py tira dele cada ilha em mm
    (stm32-cad/nucleo32.json); as ilhas SMD saem de la, os corpos sao nossos.
  - Nucleo-C031C6 (MB1717, Nucleo-64): as medidas da placa e de cada
    conector, do desenho mecanico da Nucleo-64 (manual UM1724, figura 5); a
    pinagem dos conectores (Arduino e os dois morpho de 38), da figura do
    manual UM2953; o lugar de cada componente, medido na foto de cima da
    placa (a da documentacao do Zephyr), endireitada pelos pinos do morpho.
  - Blue Pill (STM32F103C8T6): medida numa foto de cima (stm32-base.org),
    endireitada pelos quatro pinos dos cantos (passo de 2,54 mm, fileiras a
    15,24 mm). O USB fica embaixo, como no desenho anterior; o conector SWD
    de 4 pinos (3V3, DIO = PA13, CLK = PA14, GND), em cima.

O logotipo da ST e as marcas dos selos (STM32, Nucleo) nao sao desenhados.
"""
import importlib.util
import io
import json
import os
import sys

AQUI = os.path.dirname(os.path.abspath(__file__))
_spec = importlib.util.spec_from_file_location('placas_arduino', os.path.join(AQUI, 'placas-arduino.py'))
pa = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(pa)

Desenho, T, mover, rodar, f = pa.Desenho, pa.T, pa.mover, pa.rodar, pa.f
SERIGRAFIA, ESTANHO, OURO, PRETO_CI, MARCA_CI = pa.SERIGRAFIA, pa.ESTANHO, pa.OURO, pa.PRETO_CI, pa.MARCA_CI
BASE = pa.BASE
A = 96 / 25.4

AZUL, AZUL_BORDA = '#1f55ad', '#173f86'          # a mascara da Blue Pill
BRANCA, BRANCA_BORDA = '#eef0f1', '#c3c8cd'      # a das Nucleo
TINTA = '#2b2e33'                                # a serigrafia preta das Nucleo
RESISTOR, CAPACITOR = '#1e1f22', '#b7956a'
PLASTICO = '#17181b'

DEFS_EXTRA = '''  <linearGradient id="latao" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#f3d98a"/><stop offset="0.55" stop-color="#c79a3c"/><stop offset="1" stop-color="#8c6a22"/></linearGradient>
  <radialGradient id="tampa-azul" cx="0.4" cy="0.35" r="0.7"><stop offset="0" stop-color="#5a8fe0"/><stop offset="0.7" stop-color="#1f5cc0"/><stop offset="1" stop-color="#143f88"/></radialGradient>
  <radialGradient id="tampa-preta" cx="0.4" cy="0.35" r="0.7"><stop offset="0" stop-color="#5a5d63"/><stop offset="0.7" stop-color="#26282c"/><stop offset="1" stop-color="#121315"/></radialGradient>
  <linearGradient id="amarelo" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#f6d35a"/><stop offset="1" stop-color="#d49a1c"/></linearGradient>
'''


# =====================================================================
#  as pecas (em mm, no espaco local do desenho)
# =====================================================================
def pino_soldado(d, x, y, quadrado=False):
    """O pino da barra soldado por baixo, visto de cima: a ilha, a solda e a ponta."""
    if quadrado:
        d.ret_c(x, y, 1.7, 1.7, OURO, rx=0.12)
    else:
        d.circ(x, y, 0.88, OURO)
    d.circ(x, y, 0.72, 'url(#lata)', stroke='#7c838b', sw=0.05)
    d.ret_c(x, y, 0.64, 0.64, '#9aa1a8', rx=0.1)


def ilha_furo(d, x, y, r=0.75, quadrado=False):
    """Uma ilha com furo e sem pino (o conector reservado, o teste)."""
    if quadrado:
        d.ret_c(x, y, 2 * r, 2 * r, OURO, rx=0.1)
    else:
        d.circ(x, y, r, OURO)
    d.circ(x, y, r * 0.55, '#15171b')


def dois_terminais(d, cx, cy, vertical, tipo='R', comp=1.6, larg=0.8):
    """Resistor (preto) ou capacitor (castanho) 0603, com os terminais estanhados."""
    w, h = (larg, comp) if vertical else (comp, larg)
    d.ret_c(cx, cy, w, h, CAPACITOR if tipo == 'C' else RESISTOR, rx=0.05)
    d.ret_c(cx, cy, w, h, 'url(#luz)', rx=0.05)
    t = comp * 0.22
    if vertical:
        d.ret(cx - w / 2, cy - h / 2, w, t, 'url(#metal)')
        d.ret(cx - w / 2, cy + h / 2 - t, w, t, 'url(#metal)')
    else:
        d.ret(cx - w / 2, cy - h / 2, t, h, 'url(#metal)')
        d.ret(cx + w / 2 - t, cy - h / 2, t, h, 'url(#metal)')


def led_smd(d, cx, cy, vertical, comp=1.6, larg=0.8):
    """O LED SMD apagado: o corpo branco, a lente e os terminais (a luz e do simulador)."""
    w, h = (larg, comp) if vertical else (comp, larg)
    d.ret_c(cx, cy, w, h, '#f4f2ec', rx=0.08)
    d.ret_c(cx, cy, w * (0.62 if not vertical else 0.7), h * (0.7 if not vertical else 0.55), '#e2e6e9', rx=0.1)
    t = comp * 0.2
    if vertical:
        d.ret(cx - w / 2, cy - h / 2, w, t, 'url(#metal)')
        d.ret(cx - w / 2, cy + h / 2 - t, w, t, 'url(#metal)')
    else:
        d.ret(cx - w / 2, cy - h / 2, t, h, 'url(#metal)')
        d.ret(cx + w / 2 - t, cy - h / 2, t, h, 'url(#metal)')


def lqfp(d, cx, cy, corpo, n, passo, linhas, ang=0.0, perna=1.0):
    """CI de quatro lados (LQFP), n pernas por lado; a marcacao e o ponto do pino 1."""
    d.entrar(mover(cx, cy) * rodar(ang))
    m = corpo / 2
    larg = passo * 0.5
    for k in range(n):
        o = (k - (n - 1) / 2) * passo
        d.ret(o - larg / 2, -m - perna, larg, perna, 'url(#metal-h)')
        d.ret(o - larg / 2, m, larg, perna, 'url(#metal-h)')
        d.ret(-m - perna, o - larg / 2, perna, larg, 'url(#metal)')
        d.ret(m, o - larg / 2, perna, larg, 'url(#metal)')
    # (o ci() conta as linhas de baixo para cima, como no Eagle; aqui o y desce)
    pa.ci(d, corpo, corpo, list(reversed(linhas)), pino1=(-m + corpo * 0.14, -m + corpo * 0.14))
    d.sair()


def sot(d, cx, cy, ang=0.0, pernas=(2, 1), comp=2.9, larg=1.4, texto=None):
    """SOT-23 (2 pernas de um lado, 1 do outro) ou SOT-23-5/6, deitado no eixo x."""
    d.entrar(mover(cx, cy) * rodar(ang))
    for lado, n in ((-1, pernas[0]), (1, pernas[1])):
        for k in range(n):
            x = 0 if n == 1 else (k - (n - 1) / 2) * 0.95 * (2 if n == 2 else 1)
            d.ret_c(x, lado * (larg / 2 + 0.35), 0.4, 0.7, 'url(#metal-h)')
    d.ret_c(0, 0, comp, larg, PRETO_CI, rx=0.1)
    d.ret_c(0, 0, comp, larg, 'url(#luz)', rx=0.1)
    if texto:
        d.texto(0, 0, texto, larg * 0.36, MARCA_CI, ancora='middle', alinhar='center', peso='normal')
    d.sair()


def sot223(d, cx, cy, ang=0.0):
    """O regulador SOT-223: a aba larga de um lado, tres pernas do outro."""
    d.entrar(mover(cx, cy) * rodar(ang))
    d.ret_c(0, -2.45, 3.0, 1.4, ESTANHO, rx=0.1)
    d.ret_c(0, -2.2, 3.0, 1.0, 'url(#metal-h)')
    for k in (-1, 0, 1):
        d.ret_c(k * 2.3, 2.55, 0.7, 1.5, ESTANHO)
        d.ret_c(k * 2.3, 2.35, 0.6, 1.1, 'url(#metal-h)')
    d.ret_c(0, 0, 6.5, 3.5, PRETO_CI, rx=0.15)
    d.ret_c(0, 0, 6.5, 3.5, 'url(#luz)', rx=0.15)
    d.sair()


def usb_micro(d, cx, topo, fundo, boca_em_cima=True):
    """O micro-USB B de corpo metalico; a boca no lado de cima (ou de baixo)."""
    w = 7.5
    d.ret(cx - w / 2, topo, w, fundo - topo, 'url(#metal-h)', rx=0.25, stroke='#5d646c', sw=0.08)
    boca = topo if boca_em_cima else fundo - 0.55
    d.ret(cx - 3.2, boca, 6.4, 0.55, '#5b6168')
    meio = (topo + fundo) / 2
    d.linha(cx - 3.1, meio + (0.6 if boca_em_cima else -0.6), cx + 3.1, meio + (0.6 if boca_em_cima else -0.6), '#8d949b', 0.1)
    for s in (-1, 1):
        d.ret_c(cx + s * 3.0, meio - (0.3 if boca_em_cima else -0.3), 0.9, 1.6, None, rx=0.2, stroke='#8d949b', sw=0.1)


def botao_tatil(d, cx, cy, lado, tampa, r_tampa, pernas=True):
    """A chave tatil de 6 mm (THT) vista de cima: o corpo, as quatro pernas e a tampa redonda."""
    m = lado / 2
    if pernas:
        for sx in (-1, 1):
            for sy in (-1, 1):
                d.ret_c(cx + sx * (m + 0.25), cy + sy * (m - 1.0), 1.1, 0.8, ESTANHO, rx=0.1)
    d.ret_c(cx, cy, lado, lado, '#2a2c30', rx=0.3)
    d.ret_c(cx, cy, lado - 0.5, lado - 0.5, 'url(#metal)', rx=0.25, op=0.25)
    for sx in (-1, 1):
        for sy in (-1, 1):
            d.circ(cx + sx * (m - 0.8), cy + sy * (m - 0.8), 0.32, '#4a4d52')
    d.circ(cx, cy, r_tampa, tampa, stroke='#0d0e10', sw=0.08)


def cristal_hc49(d, cx, cy, comp, larg, texto, vertical=False):
    """O cristal HC-49/S: a lata de pontas redondas, com a moldura e a frequencia."""
    w, h = (larg, comp) if vertical else (comp, larg)
    r = min(w, h) / 2
    d.ret_c(cx, cy, w, h, 'url(#metal)', rx=r, stroke='#7c838b', sw=0.08)
    d.ret_c(cx, cy, w - 0.7, h - 0.7, None, rx=r - 0.35, stroke='#c9ced3', sw=0.12)
    d.texto(cx, cy, texto, min(w, h) * 0.32, '#5a5f66', ang=90 if vertical else 0, ancora='middle', alinhar='center',
            peso='normal')


def cristal_smd(d, cx, cy, w, h, texto=None):
    """O cristal SMD de tampa metalica (3225, 2012)."""
    d.ret_c(cx, cy, w, h, '#8f7b52', rx=0.15)
    d.ret_c(cx, cy, w - 0.35, h - 0.35, 'url(#metal)', rx=0.12)
    if texto:
        d.texto(cx, cy, texto, min(w, h) * 0.28, '#5a5f66', ancora='middle', alinhar='center', peso='normal')


def barra_macho(d, x0, y0, colunas, linhas, passo=2.54):
    """Barra macho vista de cima (os pinos para cima): o plastico preto e a ponta de cada pino."""
    w, h = colunas * passo, linhas * passo
    d.ret(x0 - passo / 2, y0 - passo / 2, w, h, 'url(#plastico)', rx=0.2)
    for i in range(colunas):
        for j in range(linhas):
            x, y = x0 + i * passo, y0 + j * passo
            d.ret_c(x, y, 1.6, 1.6, '#202226', rx=0.35)
            d.ret_c(x, y, 0.84, 0.84, '#7a5c18')
            d.ret_c(x, y, 0.64, 0.64, 'url(#ouro)')


def barra_femea(d, x, y0, n, passo=2.54):
    """Barra femea (a do Arduino), na vertical: o plastico e a boca quadrada de cada pino."""
    d.ret(x - 1.27, y0 - 1.27, 2.54, n * passo, 'url(#plastico)', rx=0.25, stroke='#0a0a0b', sw=0.08)
    for j in range(n):
        y = y0 + j * passo
        d.ret_c(x, y, 1.24, 1.24, '#0a0a0a')
        d.ret_c(x, y, 0.62, 0.62, '#1a1a1b')
        d.ret_c(x, y + 0.15, 0.62, 0.22, '#b8913a', op=0.6)


def jumper(d, cx, cy, w, h, cor='#1b1c1f', grad=None):
    """O jumper (o capacete que fecha dois pinos), visto de cima."""
    d.ret_c(cx, cy, w, h, grad or cor, rx=0.35, stroke='#0b0b0c', sw=0.06)
    d.ret_c(cx, cy, w - 0.6, h - 0.6, None, rx=0.25, stroke='#ffffff', sw=0.05)


def svg_de(d, larg_px, alt_px, sobra_x, sobra_y):
    return (f'<svg xmlns="http://www.w3.org/2000/svg" version="1.1" width="{f(larg_px + 2 * sobra_x)}px" '
            f'height="{f(alt_px + 2 * sobra_y)}px" data-sobra="{f(sobra_x)} {f(sobra_y)}" '
            f'viewBox="0 0 {f(larg_px + 2 * sobra_x)} {f(alt_px + 2 * sobra_y)}">\n'
            f'<defs>\n{pa.DEFS}{DEFS_EXTRA}</defs>\n' + ''.join(d.partes) + '</svg>\n')


def gravar_svg(id_peca, svg):
    io.open(BASE + f'app/assets/{id_peca}.svg', 'w', encoding='utf-8', newline='').write(svg)
    print(f'{id_peca}: {len(svg) // 1024} KB')


# =====================================================================
#  Blue Pill (desenhada nas medidas da foto: X ao longo da placa a partir
#  da ponta do USB, Y da fileira de cima da foto -- a do VB -- para baixo)
# =====================================================================
BP_L, BP_W = 52.5, 21.6
BP_FILA1 = ['G', 'G', '3.3', 'R', 'B11', 'B10', 'B1', 'B0', 'A7', 'A6', 'A5', 'A4', 'A3', 'A2', 'A1', 'A0',
            'C15', 'C14', 'C13', 'VB']
BP_FILA2 = ['B12', 'B13', 'B14', 'B15', 'A8', 'A9', 'A10', 'A11', 'A12', 'A15', 'B3', 'B4', 'B5', 'B6', 'B7',
            'B8', 'B9', '5V', 'G', '3.3']
BP_X0, BP_Y1, BP_Y2 = 2.2, 3.2, 3.2 + 15.24      # o primeiro pino de cada fileira (o da ponta do USB)
BP_SWD_X, BP_SWD = 46.5, [('GND.4', 6.99), ('SWCLK', 9.53), ('SWDIO', 12.07), ('3V3.3', 14.61)]


def nome_bp(rotulo, usados):
    """O nome do pino no simulador ("B11" -> "PB11", "G" -> "GND", "R" -> "RST"), numerado se repetir."""
    base = {'G': 'GND', '3.3': '3V3', 'R': 'RST', 'VB': 'VB', '5V': '5V'}.get(rotulo, 'P' + rotulo)
    usados[base] = usados.get(base, 0) + 1
    return base if usados[base] == 1 else f'{base}.{usados[base]}'


def gerar_bluepill():
    id_peca = 'stm32-bluepill'
    sobra_y = round(3.6 * A + 0.6, 1)
    base = T(A, 0, 0, A, 0, sobra_y)
    d = Desenho(base * T(0, -1, 1, 0, 0, BP_L))        # foto (USB a esquerda) -> simulador (USB embaixo)

    # ---- a placa ----
    d.ret(0, 0, BP_L, BP_W, AZUL, rx=0.6, stroke=AZUL_BORDA, sw=0.12)
    d.ret(0, 0, BP_L, BP_W, 'url(#luz)', rx=0.6)

    # ---- a serigrafia: as molduras das fileiras e os nomes ----
    for y in (BP_Y1, BP_Y2):
        d.ret(0.75, y - 1.45, 51.2, 2.9, None, stroke=SERIGRAFIA, sw=0.18)
    for j, (r1, r2) in enumerate(zip(BP_FILA1, BP_FILA2)):
        x = BP_X0 + j * 2.54
        d.texto(x, 1.15, r1, 1.05, SERIGRAFIA, ancora='middle', alinhar='center', peso='normal')
        d.texto(x, BP_W - 1.15, r2, 1.05, SERIGRAFIA, ancora='middle', alinhar='center', peso='normal')
    d.texto(19.6, 13.6, 'RESET', 1.0, SERIGRAFIA, ang=-90, ancora='middle', alinhar='center', peso='normal')
    d.texto(42.2, 5.6, 'PWR', 0.85, SERIGRAFIA, ancora='middle', alinhar='center', peso='normal')
    d.texto(42.2, 16.0, 'PC13', 0.85, SERIGRAFIA, ancora='middle', alinhar='center', peso='normal')
    d.texto(45.9, 9.9, '+', 0.9, SERIGRAFIA, ancora='middle', alinhar='center', peso='normal')
    d.texto(45.9, 11.5, '+', 0.9, SERIGRAFIA, ancora='middle', alinhar='center', peso='normal')
    d.texto(18.9, 6.4, '1', 0.85, SERIGRAFIA, ancora='middle', alinhar='center', peso='normal')
    d.ret(9.0, 5.0, 9.8, 5.7, None, stroke=SERIGRAFIA, sw=0.12)       # a moldura do BOOT0/BOOT1

    # ---- os 40 pinos da barra (o quadrado e o pino 1 de cada fileira) ----
    for j in range(20):
        x = BP_X0 + j * 2.54
        pino_soldado(d, x, BP_Y1, quadrado=j == 19)
        pino_soldado(d, x, BP_Y2, quadrado=j == 19)

    # ---- o micro-USB (a boca para fora da placa) e as abas soldadas ----
    for x, y in ((1.6, 5.8), (5.3, 5.8), (1.6, 15.6), (5.3, 15.6)):
        d.ret_c(x, y, 1.9, 1.7, ESTANHO, rx=0.1)
    d.entrar(mover(2.1, 10.6) * rodar(-90))
    usb_micro(d, 0, -3.15, 2.1, boca_em_cima=True)
    d.sair()

    # ---- BOOT0 e BOOT1: a barra 2x3 e os dois jumpers na posicao 0 ----
    barra_macho(d, 11.0, 6.55, 3, 2)
    for y in (6.55, 9.09):
        jumper(d, 12.27, y, 4.6, 2.2, grad='url(#amarelo)')

    # ---- RESET: a chave SMD de moldura metalica e o botao de latao ----
    for sx in (-1, 1):
        for sy in (-1, 1):
            d.ret_c(15.1 + sx * 2.35, 13.6 + sy * 1.7, 0.9, 0.9, ESTANHO, rx=0.1)
    d.ret_c(15.1, 13.6, 4.4, 4.4, 'url(#metal)', rx=0.35, stroke='#7c838b', sw=0.08)
    d.ret_c(15.1, 13.6, 3.4, 3.4, '#d5d9dd', rx=0.3)
    d.circ(15.1, 13.6, 1.05, 'url(#latao)', stroke='#7a5a1c', sw=0.06)

    # ---- o STM32F103C8T6 (LQFP48, girado 45 graus, como na placa) ----
    lqfp(d, 26.45, 10.7, 7.0, 12, 0.5, [('STM32', 0.75), ('F103C8T6', 0.62), ('GH21K 93', 0.5)], ang=45)

    # ---- os cristais: 8 MHz (HC-49/S) e 32,768 kHz ----
    for y in (6.1, 15.3):
        d.ret_c(35.7, y, 1.2, 1.3, ESTANHO)
    cristal_hc49(d, 35.7, 10.7, 10.5, 4.65, '8.000', vertical=True)
    for x in (39.0, 41.9):
        for y in (7.2, 14.2):
            d.ret_c(x, y, 0.9, 1.2, ESTANHO)
    d.ret_c(40.45, 10.7, 3.6, 7.6, PRETO_CI, rx=0.25)
    d.ret_c(40.45, 10.7, 3.6, 7.6, 'url(#luz)', rx=0.25)
    d.texto(40.45, 10.7, '32.768K', 0.62, MARCA_CI, ang=90, ancora='middle', alinhar='center', peso='normal')

    # ---- os dois LEDs 0805 (PWR e PC13) ----
    led_smd(d, 44.7, 7.1, True, 2.0, 1.25)
    led_smd(d, 44.7, 14.3, True, 2.0, 1.25)

    # ---- o SWD: as quatro ilhas e a barra em angulo reto, para fora da placa ----
    for _, y in BP_SWD:
        pino_soldado(d, BP_SWD_X, y, quadrado=False)
    d.ret(47.95, 5.7, 3.15, 10.0, 'url(#plastico)', rx=0.2)
    for _, y in BP_SWD:
        d.ret(47.2, y - 0.32, 0.8, 0.64, '#9aa1a8')
        d.ret(51.1, y - 0.32, 5.0, 0.64, 'url(#metal)', rx=0.12)
        d.ret_c(48.0, y, 0.25, 0.64, '#6c7278')

    # ---- os pinos, nas coordenadas do simulador ----
    def sim(X, Y):
        return round(Y * A, 2), round((BP_L - X) * A, 2)

    usados = {}
    esquerda = [(nome_bp(r, usados), BP_X0 + j * 2.54, BP_Y1) for j, r in reversed(list(enumerate(BP_FILA1)))]
    direita = [(nome_bp(r, usados), BP_X0 + j * 2.54, BP_Y2) for j, r in reversed(list(enumerate(BP_FILA2)))]
    pinos = [(n, *sim(X, Y)) for n, X, Y in esquerda + direita]
    pinos += [(n, *sim(BP_SWD_X, Y)) for n, Y in BP_SWD]
    tamanho = (round(BP_W * A), round(BP_L * A))
    gravar_svg(id_peca, svg_de(d, tamanho[0], tamanho[1], 0, sobra_y))
    print('  PWR:', sim(44.7, 7.1), ' PC13:', sim(44.7, 14.3), ' RESET:', sim(15.1, 13.6))
    return id_peca, pinos, tamanho


# =====================================================================
#  Nucleo-L031K6 (Nucleo-32, MB1180)
# =====================================================================
N32_L, N32_A = 18.54, 50.29
N32_X = (1.651, 16.889)
N32_Y0 = 8.854
N32_CN3 = ['D1', 'D0', 'RST.2', 'GND.2', 'D2', 'D3', 'D4', 'D5', 'D6', 'D7', 'D8', 'D9', 'D10', 'D11', 'D12']
N32_CN4 = ['VIN', 'GND', 'RST', '5V', 'A7', 'A6', 'A5', 'A4', 'A3', 'A2', 'A1', 'A0', 'AREF', '3V3', 'D13']
N32_RSERIG3 = ['D1/TX', 'D0/RX', 'RST', 'GND', 'D2', 'D3', 'D4', 'D5', 'D6', 'D7', 'D8', 'D9', 'D10', 'D11', 'D12']
N32_RSERIG4 = ['VIN', 'GND', 'RST', '5V', 'A7', 'A6', 'A5', 'A4', 'A3', 'A2', 'A1', 'A0', 'REF', '3V3', 'D13']
# os capacitores (pelo nome da serigrafia, que fica embaixo de cada um); o resto e resistor
N32_CAPACITORES = [(3.93, 12.85), (14.65, 19.6), (8.63, 23.15), (14.62, 23.15), (11.4, 41.55), (3.85, 41.55)]


def gerar_nucleo32():
    id_peca = 'st-nucleo-l031k6'
    dados = json.load(io.open(os.path.join(AQUI, 'stm32-cad', 'nucleo32.json'), encoding='utf-8'))
    sy = N32_A / dados['placa_mm'][1]
    usb_topo = -1.1
    sobra_y = round(-usb_topo * A + 0.6, 1)
    d = Desenho(T(A, 0, 0, A, 0, sobra_y))

    d.ret(0, 0, N32_L, N32_A, BRANCA, rx=0.5, stroke=BRANCA_BORDA, sw=0.12)
    d.ret(0, 0, N32_L, N32_A, 'url(#luz)', rx=0.5)

    # ---- as ilhas SMD do desenho (as das letras e as do QFN alternativo ficam de fora) ----
    pares = []
    for e in dados['desenhos']:
        if not e['cheio'] or e['tipos'] not in ('re', 'llll', 'lll', 'lllll', 'lllllll', 'llllllll'):
            continue
        x0, y0, x1, y1 = e['x0'], e['y0'] * sy, e['x1'], e['y1'] * sy
        w, h = x1 - x0, y1 - y0
        if w < 0.2 or h < 0.2 or w > 4 or h > 4:
            continue
        if e['tipos'] != 're' and max(w, h) < 0.75:              # as letras da serigrafia
            continue
        cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
        if 6.6 < cx < 11.95 and 28.6 < cy < 34.6:                  # o QFN que nao e montado (sob o LQFP)
            continue
        if any(abs(cx - x) < 1.2 for x in N32_X) and cy > 7.5:     # as ilhas da barra (ja desenhadas)
            continue
        if cy < 7.2 and cx > 14:                                    # o conector SWD (redondo, abaixo)
            continue
        d.ret(x0, y0, w, h, ESTANHO, rx=0.05)
        if e['tipos'] == 're' and 0.95 < w < 1.05 and 0.85 < h < 0.95:
            pares.append((cx, cy, True))                           # a ilha de um 0603 em pe
        elif e['tipos'] == 're' and 0.85 < w < 0.95 and 0.95 < h < 1.05:
            pares.append((cx, cy, False))                          # a de um 0603 deitado

    # ---- os 0603 entre cada par de ilhas: primeiro os de pe, depois os deitados ----
    usados = set()
    for de_pe in (True, False):
        for i, (ax, ay, va) in enumerate(pares):
            for j, (bx, by, vb) in enumerate(pares):
                if j <= i or i in usados or j in usados or va != de_pe or vb != de_pe:
                    continue
                junto = (abs(ax - bx) < 0.15 and 1.3 < abs(ay - by) < 1.9) if de_pe else                         (abs(ay - by) < 0.15 and 1.3 < abs(ax - bx) < 1.9)
                if not junto:
                    continue
                cx, cy = (ax + bx) / 2, (ay + by) / 2
                # (os deitados sao o C12 e o C13, perto do cristal)
                tipo = 'C' if not de_pe or any(abs(cx - x) < 0.6 and abs(cy - y) < 0.9 for x, y in N32_CAPACITORES) else 'R'
                dois_terminais(d, cx, cy, de_pe, tipo, comp=1.6, larg=0.85)
                usados |= {i, j}

    # ---- a serigrafia ----
    for k, (r3, r4) in enumerate(zip(N32_RSERIG3, N32_RSERIG4)):
        y = N32_Y0 + k * 2.54
        for x, r in ((2.95, r3), (15.62, r4)):
            d.texto(x, y, r, 0.7 if len(r) <= 3 else 0.56, TINTA, ang=-90, ancora='middle', alinhar='center', peso='normal')
    for t, x, y in (('CN3', 1.5, 7.2), ('CN4', 16.9, 7.25), ('LD1', 1.9, 0.75), ('LD2', 2.4, 49.4), ('LD3', 16.3, 49.4),
                    ('B1', 9.3, 45.3), ('CN1', 15.0, 0.55), ('U2', 4.2, 26.0), ('X1', 5.0, 40.5), ('U3', 13.67, 43.75),
                    ('T1', 10.1, 7.6), ('D1', 8.15, 6.05), ('D2', 8.15, 7.7)):
        d.texto(x, y, t, 0.72, TINTA, ancora='middle', alinhar='center', peso='normal')

    # ---- as barras (soldadas por baixo: aqui so a ilha e a ponta) ----
    for k in range(15):
        y = N32_Y0 + k * 2.54
        pino_soldado(d, N32_X[0], y, quadrado=k == 0)
        pino_soldado(d, N32_X[1], y, quadrado=k == 0)

    # ---- o conector SWD do ST-LINK (reservado: so as ilhas) e as ancoras do USB ----
    for x, y in ((17.372, 1.116), (17.372, 3.652), (14.832, 3.652), (14.832, 6.188)):
        ilha_furo(d, x, y, 0.72)
    ilha_furo(d, 17.36, 6.2, 0.72, quadrado=True)
    for x in (6.781, 11.784):
        d.circ(x, 4.159, 0.72, ESTANHO)

    # ---- o micro-USB do ST-LINK (a boca para fora, em cima) ----
    usb_micro(d, 9.28, usb_topo, 4.3, boca_em_cima=True)

    # ---- LD1 (COM, vermelho e verde), os diodos, o T1, o U1 ----
    d.ret_c(1.92, 3.44, 2.0, 2.0, '#f4f2ec', rx=0.15)
    d.ret_c(1.92, 3.44, 1.3, 1.3, '#e3dfe6', rx=0.4)
    for cy in (5.95, 7.85):
        d.ret_c(5.6, cy, 2.7, 1.5, PRETO_CI, rx=0.15)
        d.ret_c(5.6, cy, 2.7, 1.5, 'url(#luz)', rx=0.15)
        d.ret_c(4.55, cy, 0.35, 1.5, '#8d9298')
    sot(d, 10.55, 9.33, ang=90, pernas=(2, 1))
    d.entrar(mover(9.27, 18.4))
    for k in range(4):
        o = (k - 1.5) * 1.27
        d.ret_c(o, -2.55, 0.45, 1.1, 'url(#metal-h)')
        d.ret_c(o, 2.55, 0.45, 1.1, 'url(#metal-h)')
    pa.ci(d, 4.9, 3.9, [], pino1=(-2.0, 1.3))
    d.sair()

    # ---- o STM32L031K6 (LQFP32) ----
    lqfp(d, 9.27, 31.56, 7.0, 8, 0.8, [('STM32', 0.8), ('L031K6T6', 0.66), ('GH24Q 97', 0.52)], perna=1.0)

    # ---- X1, U3 (o regulador de 3,3 V), o botao de RESET, LD2 e LD3 ----
    cristal_smd(d, 7.67, 41.2, 3.2, 1.5)
    d.ret_c(13.67, 42.0, 2.0, 2.0, PRETO_CI, rx=0.1)
    d.ret_c(13.67, 42.0, 2.0, 2.0, 'url(#luz)', rx=0.1)
    d.ret_c(9.35, 48.15, 6.2, 3.6, 'url(#metal)', rx=0.3, stroke='#7c838b', sw=0.08)
    d.ret_c(9.35, 48.15, 4.0, 2.4, '#2a2c30', rx=0.35)
    d.ret_c(9.35, 48.15, 2.6, 1.5, 'url(#tampa-preta)', rx=0.3)
    led_smd(d, 4.35, 47.7, True, 2.0, 1.25)
    led_smd(d, 14.35, 47.7, True, 2.0, 1.25)

    pinos = [(n, round(N32_X[0] * A, 2), round((N32_Y0 + k * 2.54) * A, 2)) for k, n in enumerate(N32_CN3)]
    pinos += [(n, round(N32_X[1] * A, 2), round((N32_Y0 + k * 2.54) * A, 2)) for k, n in enumerate(N32_CN4)]
    tamanho = (round(N32_L * A), round(N32_A * A))
    gravar_svg(id_peca, svg_de(d, tamanho[0], tamanho[1], 0, sobra_y))
    print('  LD1:', (round(1.92 * A, 1), round(3.44 * A, 1)), ' LD2:', (round(4.35 * A, 1), round(47.7 * A, 1)),
          ' LD3:', (round(14.35 * A, 1), round(47.7 * A, 1)), ' B1:', (round(9.35 * A, 1), round(48.15 * A, 1)))
    return id_peca, pinos, tamanho


# =====================================================================
#  Nucleo-C031C6 (Nucleo-64, MB1717)
# =====================================================================
N64_L, N64_A = 70.0, 82.5
N64_MORPHO_Y0 = 31.2
N64_CN7_X = (3.20, 5.74)
N64_CN10_X = (64.16, 66.70)
N64_ARD_X = (10.87, 59.13)
N64_CN7 = ['PD0', 'PD1', 'PD2', 'PD2.2', 'VDD', 'E5V', 'PA14', 'GND.4', 'NC.2', 'NC.3', 'NC.4', 'IOREF.2', 'PA13',
           'RST.2', 'NC.5', '3V3.2', 'PC6', '5V.2', 'GND.5', 'GND.6', 'NC.6', 'GND.7', 'PC13', 'VIN.2', 'PC14',
           'NC.7', 'NC.8', 'PA0', 'PF0', 'PA1', 'PF1', 'PA4', 'NC.9', 'PB1', 'PB11', 'PA11', 'PA2', 'PA12']
N64_CN10 = ['PD3', 'NC.10', 'PB8', 'NC.11', 'PB9', 'NC.12', 'AVDD.2', '5V_USB_CHG', 'GND.8', 'PA3', 'PA5', 'PC15',
            'PA6', 'PB12', 'PA7', 'PB12.2', 'PB0', 'PB2', 'PC7', 'GND.9', 'PA9', 'PF3', 'PA15', 'PA8', 'PB5', 'PB15',
            'PB4', 'PB14', 'PB10', 'PB13', 'PB3', 'AGND', 'PA10', 'PB0.2', 'PB6', 'NC.13', 'PB7', 'NC.14']
N64_CN6 = ['NC', 'IOREF', 'RST', '3V3', '5V', 'GND', 'GND.2', 'VIN']                    # y 41.3 .. 59.08
N64_CN8 = ['A0', 'A1', 'A2', 'A3', 'A4', 'A5']                                          # y 64.2 .. 76.9
N64_CN5 = ['D15', 'D14', 'AVDD', 'GND.3', 'D13', 'D12', 'D11', 'D10', 'D9', 'D8']        # y 32.2 .. 55.06
N64_CN9 = ['D7', 'D6', 'D5', 'D4', 'D3', 'D2', 'D1', 'D0']                              # y 59.12 .. 76.9
N64_SERIG_ESQ = ['', 'IOREF', 'NRST', '3V3', '5V', 'GND', 'GND', 'VIN']
N64_SERIG_DIR = ['SCL/D15', 'SDA/D14', 'AVDD', 'GND', 'SCK/D13', 'MISO/D12', 'PWM/MOSI/D11', 'PWM/CS/D10', 'PWM/D9',
                 'D8', 'D7', 'PWM/D6', 'PWM/D5', 'D4', 'PWM/D3', 'D2', 'TX/D1', 'RX/D0']


def contorno_n64():
    """A placa: os cantos de cima redondos, o degrau de baixo (o formato do shield)."""
    r = 1.6
    return [('M', r, 0), ('L', N64_L - r, 0), ('A', r, 0, 1, N64_L, r), ('L', N64_L, 80.0 - r),
            ('A', r, 0, 1, N64_L - r, 80.0), ('L', 49.0, 80.0), ('L', 46.5, N64_A), ('L', 15.5, N64_A),
            ('L', 13.0, 80.0), ('L', r, 80.0), ('A', r, 0, 1, 0, 80.0 - r), ('L', 0, r), ('A', r, 0, 1, r, 0), ('Z',)]


def gerar_nucleo64():
    id_peca = 'st-nucleo-c031c6'
    sobra_y = round(0.6 * A + 0.6, 1)
    d = Desenho(T(A, 0, 0, A, 0, sobra_y))
    d.caminho(contorno_n64(), BRANCA, stroke=BRANCA_BORDA, sw=0.15)
    d.caminho(contorno_n64(), 'url(#luz)')
    # o corte entre o ST-LINK e a placa (as fendas de quebrar)
    for x0, x1 in ((1.0, 17.6), (20.0, 46.0), (49.0, 69.0)):
        d.ret(x0, 24.0, x1 - x0, 0.9, '#d6dadd', rx=0.45, stroke='#b5bbc1', sw=0.06)
    for x, y in ((10.8, 27.6), (58.7, 29.0), (43.4, 78.6)):
        d.circ(x, y, 1.6, '#e6e9eb', stroke='#aab0b6', sw=0.1)
        d.circ(x, y, 1.45, '#15171b', op=0.08)

    def txt(x, y, t, tam=0.95, ang=0, ancora='middle'):
        d.texto(x, y, t, tam, TINTA, ang=ang, ancora=ancora, alinhar='center' if ancora == 'middle' else
                ('center-left' if ancora == 'start' else 'center-right'), peso='normal')

    # ---------------- o ST-LINK (em cima) ----------------
    barra_macho(d, 2.5, 3.2, 1, 2)
    txt(2.5, 0.9, 'JP2', 0.8)
    txt(4.7, 4.5, 'GND', 0.8, ang=-90)
    for k, (y, t) in enumerate(((3.8, 'R1'), (5.3, 'R2'))):
        dois_terminais(d, 12.9, y, False)
        txt(15.3, y, t, 0.75)
    d.ret_c(13.3, 8.5, 1.6, 0.8, PRETO_CI, rx=0.1)
    txt(15.3, 8.5, 'D2', 0.75)
    barra_macho(d, 8.9, 8.2, 1, 6)
    txt(6.5, 13.0, 'SWD', 0.9, ang=90)
    txt(6.6, 22.4, 'CN3', 0.8)
    for y, t in ((11.6, 'R15'), (13.1, 'R18'), (14.6, 'R19'), (16.1, 'R20')):
        dois_terminais(d, 12.9, y, False)
        txt(15.5, y, t, 0.75)
    for y, t in ((18.4, 'TX'), (21.0, 'RX')):
        ilha_furo(d, 16.0, y, 0.8, quadrado=t == 'RX')
        txt(18.3, y, t, 0.9)
    d.ret(14.9, 17.2, 2.2, 5.0, None, stroke=TINTA, sw=0.1)
    txt(13.9, 19.7, 'JP4', 0.75, ang=90)
    # CN2: os dois jumpers ST-LINK/NUCLEO
    barra_macho(d, 22.55, 2.6, 1, 4, passo=2.54)
    jumper(d, 22.55, 3.87, 2.4, 4.6)
    jumper(d, 22.55, 8.95, 2.4, 4.6)
    d.ret(19.6, 1.6, 0.55, 8.6, '#3767b5', rx=0.25)
    d.ret(24.95, 1.6, 0.55, 8.6, '#3767b5', rx=0.25)
    txt(18.7, 6.0, 'ST-LINK', 0.8, ang=90)
    txt(26.4, 6.0, 'NUCLEO', 0.8, ang=90)
    txt(22.3, 11.6, 'CN2', 0.75)
    cristal_smd(d, 27.6, 14.6, 3.2, 2.5)
    txt(27.6, 12.6, 'X1', 0.75)
    for y, t in ((13.3, 'C4'), (15.6, 'C5')):
        dois_terminais(d, 25.4, y, False, 'C')
        txt(23.3, y, t, 0.75)
    dois_terminais(d, 28.4, 18.6, False, 'C')
    txt(26.2, 18.6, 'C10', 0.75)
    for x, t, tipo in ((30.6, 'R3', 'R'), (31.9, 'R4', 'R'), (33.2, 'C1', 'C'), (34.5, 'R5', 'R'), (35.8, 'SB1', 'S'),
                       (37.1, 'R6', 'R'), (38.4, 'R7', 'R'), (42.8, 'R13', 'R'), (44.1, 'R8', 'R')):
        if tipo == 'S':
            d.ret_c(x, 7.0, 0.8, 0.6, ESTANHO)
            d.ret_c(x, 8.0, 0.8, 0.6, ESTANHO)
        else:
            dois_terminais(d, x, 7.5, True, tipo)
        txt(x, 5.4, t, 0.62, ang=90)
    d.ret_c(40.0, 7.5, 0.8, 1.6, PRETO_CI, rx=0.1)
    d.ret_c(40.0, 6.9, 0.8, 0.25, '#8d9298')
    txt(40.0, 5.4, 'D1', 0.62, ang=90)
    lqfp(d, 35.7, 13.6, 7.0, 12, 0.5, [('STM32', 0.75), ('F103CBT6', 0.62)])
    txt(30.6, 9.4, 'U2', 0.75)
    dois_terminais(d, 40.6, 11.2, False, 'C')
    txt(40.6, 10.0, 'C3', 0.7)
    for y, t, tipo in ((15.4, 'R21', 'R'), (17.3, 'C9', 'C')):
        dois_terminais(d, 41.9, y, False, tipo)
        txt(41.9, y + 1.15 if t == 'C9' else y - 1.15, t, 0.7)
    for y in (16.0, 17.8, 19.6):
        sot(d, 45.6, y, pernas=(1, 2), comp=2.0, larg=1.2)
    for y, t in ((16.0, 'D3'), (17.8, 'D4'), (19.6, 'D5')):
        txt(47.9, y, t, 0.7)
    # CN1, JP1, T1, U1, LD1, JP3
    usb_micro(d, 46.4, -0.4, 4.6, boca_em_cima=True)
    txt(51.2, 3.4, 'CN1', 0.75)
    txt(54.6, 0.9, 'USB_STLINK', 0.75)
    barra_macho(d, 34.4, 2.6, 2, 1)
    txt(36.2, 0.85, 'JP1', 0.75)
    txt(39.7, 3.9, 'STLK_RST', 0.7)
    sot(d, 55.3, 4.6, pernas=(2, 1))
    txt(53.3, 2.6, 'T1', 0.75)
    sot(d, 48.3, 8.4, pernas=(3, 2), comp=2.9, larg=1.6)
    txt(48.3, 10.6, 'U1', 0.75)
    d.ret_c(60.3, 4.0, 1.7, 1.7, '#f4f2ec', rx=0.15)
    d.ret_c(60.3, 4.0, 1.1, 1.1, '#e3dfe6', rx=0.35)
    txt(60.3, 1.4, 'LD1', 0.8)
    txt(62.4, 4.3, 'COM', 0.75, ang=-90)
    barra_macho(d, 65.6, 3.0, 1, 2)
    txt(65.6, 0.9, 'JP3', 0.8)
    txt(63.6, 4.8, 'GND', 0.75, ang=-90)
    for x, t, tipo in ((52.5, 'C2', 'C'), (53.8, 'R14', 'R'), (55.1, 'R9', 'R'), (56.4, 'R10', 'R'),
                       (60.4, 'R11', 'R'), (61.7, 'R12', 'R'), (64.4, 'R16', 'R'), (67.2, 'R17', 'R')):
        dois_terminais(d, x, 11.4, True, tipo)
        txt(x, 9.2, t, 0.6, ang=90)
    led_smd(d, 65.8, 11.1, True, 1.6, 0.8)
    txt(65.8, 13.6, 'LD2', 0.6, ang=90)
    sot(d, 54.2, 16.6, pernas=(3, 3), comp=2.9, larg=1.6)
    txt(54.2, 14.4, 'U4', 0.75)
    for x, t in ((56.9, 'C7'), (58.2, 'C8')):
        dois_terminais(d, x, 16.6, True, 'C')
        txt(x, 18.9, t, 0.6, ang=90)
    for y, t in ((19.6, 'C11'), (20.8, 'C12')):
        dois_terminais(d, 51.3, y, False, 'C')
        txt(49.1, y, t, 0.6)
    dois_terminais(d, 53.2, 20.2, True, 'C')
    dois_terminais(d, 59.6, 16.0, True, 'C')
    txt(59.6, 13.8, 'C6', 0.6, ang=90)
    sot(d, 63.2, 17.3, pernas=(3, 2), comp=2.9, larg=1.6)
    txt(63.2, 15.2, 'U3', 0.75)
    dois_terminais(d, 60.2, 20.2, True, 'C')
    dois_terminais(d, 67.1, 17.7, True, 'R')
    for x, t, tipo in ((29.4, 'R23', 'R'), (31.5, 'R24', 'R'), (36.6, 'C15', 'C'), (39.1, 'R25', 'R')):
        dois_terminais(d, x, 20.9, True, tipo)
        txt(x, 23.0, t, 0.6, ang=90)

    # ---------------- a placa de baixo (a Nucleo) ----------------
    botao_tatil(d, 27.0, 28.0, 6.0, 'url(#tampa-azul)', 1.8)
    txt(22.6, 32.4, 'B1', 0.85)
    txt(31.6, 27.5, 'USER', 0.85, ang=-90)
    botao_tatil(d, 45.6, 28.0, 6.0, 'url(#tampa-preta)', 1.8)
    txt(41.0, 32.6, 'B2', 0.85)
    txt(40.2, 27.6, 'RESET', 0.85, ang=-90)
    for x, y, t in ((20.2, 26.3, 'R26'), (20.3, 30.6, 'R29')):
        dois_terminais(d, x, y, True)
    dois_terminais(d, 21.6, 30.6, True, 'C')
    led_smd(d, 53.4, 25.7, True, 1.6, 0.8)
    led_smd(d, 56.4, 25.7, True, 1.6, 0.8)
    dois_terminais(d, 52.2, 28.6, True)
    dois_terminais(d, 55.2, 28.6, True)
    txt(51.0, 27.4, 'LD3', 0.6, ang=90)
    txt(57.8, 27.4, 'LD4', 0.6, ang=90)
    dois_terminais(d, 15.5, 35.6, False)
    txt(15.5, 34.4, 'R30', 0.65)
    dois_terminais(d, 18.0, 35.8, True, 'C')
    txt(18.0, 37.9, 'C17', 0.6)
    sot223(d, 22.2, 37.6)
    txt(22.2, 42.9, 'U5', 0.8)
    dois_terminais(d, 28.4, 40.8, True, 'C')
    txt(28.4, 42.9, 'C18', 0.6)
    barra_macho(d, 33.4, 39.0, 2, 1)
    jumper(d, 34.67, 39.0, 4.6, 2.2)
    txt(34.67, 36.9, 'JP6', 0.8)
    txt(34.67, 41.3, 'IDD', 0.85)
    barra_macho(d, 42.8, 35.0, 4, 2)
    jumper(d, 50.42, 36.27, 2.2, 4.6)
    for x, t in ((42.8, 'CHG'), (45.34, 'E5V'), (47.88, 'VIN'), (50.42, 'STLK')):
        txt(x, 31.6, t, 0.75, ang=90)
    txt(39.4, 36.3, 'JP5', 0.8)
    for x, t, tipo in ((40.2, 'SB2', 'R'), (41.5, 'C19', 'C'), (42.8, 'C20', 'C')):
        dois_terminais(d, x, 42.0, True, tipo)
    sot(d, 45.3, 42.0, pernas=(3, 2), comp=2.9, larg=1.6)
    txt(45.3, 44.2, 'U6', 0.75)
    for x in (48.2, 49.6):
        dois_terminais(d, x, 42.2, True, 'C')
    for y in (50.4, 51.4, 52.4):
        dois_terminais(d, 26.6, y, False, 'R', comp=1.0, larg=0.5)
    lqfp(d, 35.8, 57.5, 7.0, 12, 0.5, [('STM32', 0.75), ('C031C6T6', 0.62), ('GH23L 93', 0.5)])
    txt(31.2, 52.2, 'U7', 0.75)
    txt(35.8, 63.0, 'LQFP48', 0.75)
    for y in (54.9, 55.9):
        dois_terminais(d, 19.2, y, False, 'C', comp=1.0, larg=0.5)
    for y in (57.4, 58.4):
        dois_terminais(d, 23.0, y, False, 'R', comp=1.0, larg=0.5)
    cristal_smd(d, 22.0, 59.9, 2.0, 1.2)
    txt(19.6, 59.8, 'X3', 0.6, ang=90)
    dois_terminais(d, 21.8, 61.5, False, 'C', comp=1.0, larg=0.5)
    for y in (61.9, 62.9, 63.9):
        dois_terminais(d, 25.5, y, False, 'R', comp=1.0, larg=0.5)
    for x, y in ((19.3, 65.6), (21.6, 65.6), (19.3, 66.6), (21.6, 66.6), (17.2, 70.3), (17.2, 71.9), (17.2, 73.4),
                 (17.2, 74.9)):
        d.ret_c(x - 0.35, y, 0.5, 0.6, ESTANHO)
        d.ret_c(x + 0.35, y, 0.5, 0.6, ESTANHO)
    for y, t in ((70.3, 'SB7'), (71.9, 'SB8'), (73.4, 'SB9'), (74.9, 'SB10')):
        txt(18.4, y, t, 0.62, ancora='start')
    # a etiqueta do modelo
    d.ret(27.3, 67.4, 21.9, 8.8, '#fbfbf8', rx=0.2, stroke='#c8cbcf', sw=0.08)
    txt(38.25, 70.0, 'NUCLEO-C031C6', 1.35)
    txt(38.25, 73.3, 'ENUC031C6', 1.0)
    txt(30.0, 66.3, 'HW1', 0.6)
    d.ret(24.6, 77.1, 7.6, 2.0, '#2f62b1', rx=0.15)
    d.texto(28.4, 78.1, 'MB1717B', 0.95, '#ffffff', ancora='middle', alinhar='center', peso='bold')

    # ---------------- os conectores ----------------
    barra_macho(d, N64_CN7_X[0], N64_MORPHO_Y0, 2, 19)
    barra_macho(d, N64_CN10_X[0], N64_MORPHO_Y0, 2, 19)
    barra_femea(d, N64_ARD_X[0], 41.3, 8)
    barra_femea(d, N64_ARD_X[0], 64.2, 6)
    barra_femea(d, N64_ARD_X[1], 32.2, 10)
    barra_femea(d, N64_ARD_X[1], 59.12, 8)
    for x, t in ((N64_CN7_X[0], '1'), (N64_CN7_X[1], '2'), (N64_CN10_X[0], '1'), (N64_CN10_X[1], '2')):
        txt(x, 29.3, t, 0.75)
    txt(4.47, 79.0, 'CN7', 0.85)
    txt(65.43, 79.0, 'CN10', 0.85)
    txt(10.87, 60.6, 'CN6', 0.8)
    txt(10.87, 78.6, 'CN8', 0.8)
    txt(59.13, 56.6, 'CN5', 0.8)
    txt(59.13, 78.6, 'CN9', 0.8)
    txt(7.2, 31.2 + 2 * 2.54, 'E5V', 0.8, ancora='start')
    txt(7.2, 31.2 + 3 * 2.54, 'GND', 0.8, ancora='start')
    for k, t in enumerate(N64_SERIG_ESQ):
        if t:
            txt(12.6, 41.3 + k * 2.54, t, 0.85, ancora='start')
    for k in range(6):
        txt(12.6, 64.2 + k * 2.54, f'A{k}', 0.85, ancora='start')
    d.linha(15.9, 43.5, 15.9, 59.0, TINTA, 0.1)
    txt(16.7, 51.3, 'POWER', 0.75, ang=90)
    d.linha(15.9, 64.2, 15.9, 76.9, TINTA, 0.1)
    txt(16.7, 70.5, 'AIN', 0.75, ang=90)
    ys = [32.2 + k * 2.54 for k in range(10)] + [59.12 + k * 2.54 for k in range(8)]
    for y, t in zip(ys, N64_SERIG_DIR):
        txt(57.4, y, t, 0.85, ancora='end')

    pinos = []
    for k, n in enumerate(N64_CN7):
        pinos.append((n, N64_CN7_X[k % 2], N64_MORPHO_Y0 + (k // 2) * 2.54))
    for k, n in enumerate(N64_CN10):
        pinos.append((n, N64_CN10_X[k % 2], N64_MORPHO_Y0 + (k // 2) * 2.54))
    pinos += [(n, N64_ARD_X[0], 41.3 + k * 2.54) for k, n in enumerate(N64_CN6)]
    pinos += [(n, N64_ARD_X[0], 64.2 + k * 2.54) for k, n in enumerate(N64_CN8)]
    pinos += [(n, N64_ARD_X[1], 32.2 + k * 2.54) for k, n in enumerate(N64_CN5)]
    pinos += [(n, N64_ARD_X[1], 59.12 + k * 2.54) for k, n in enumerate(N64_CN9)]
    pinos = [(n, round(x * A, 2), round(y * A, 2)) for n, x, y in pinos]
    tamanho = (round(N64_L * A), round(N64_A * A))
    gravar_svg(id_peca, svg_de(d, tamanho[0], tamanho[1], 0, sobra_y))
    print('  LD1:', (round(60.3 * A, 1), round(4.0 * A, 1)), ' LD3:', (round(53.4 * A, 1), round(25.7 * A, 1)),
          ' LD4:', (round(56.4 * A, 1), round(25.7 * A, 1)), ' B1:', (round(27.0 * A, 1), round(28.0 * A, 1)),
          ' B2:', (round(45.6 * A, 1), round(28.0 * A, 1)))
    return id_peca, pinos, tamanho


# =====================================================================
#  os pinos no pecas.json
# =====================================================================
DESCRICOES = {
    'SWDIO': 'SWDIO (o DIO do conector SWD): o PA13. Depuracao pelo ST-LINK; com a depuracao desligada, e um GPIO.',
    'SWCLK': 'SWCLK (o CLK do conector SWD): o PA14. Depuracao pelo ST-LINK; com a depuracao desligada, e um GPIO.',
    'VB': 'VBAT: alimenta o relogio (RTC) e os registradores de backup com a placa desligada (uma pilha de 3 V).',
    'NC': 'Nao conectado: o pino existe no conector, mas nao vai a lugar nenhum.',
    'E5V': 'E5V: entrada de 5 V de fora (com o jumper JP5 em E5V, a placa liga por aqui).',
    '5V_USB_CHG': '5V_USB_CHG: os 5 V da USB do ST-LINK no modo carregador (JP5 em CHG).',
    'AVDD': 'AVDD: a alimentacao analogica do STM32 (3,3 V), no lugar do AREF do Arduino.',
    'VDD': 'VDD: a alimentacao do STM32 (3,3 V).',
    'AGND': 'AGND: o terra analogico (o mesmo GND da placa).',
    'IOREF': 'IOREF: a tensao dos pinos (3,3 V).',
}
PODER = ('3V3', '5V', 'GND', 'VIN', 'VDD', 'AVDD', 'AGND', 'IOREF')


def gravar_pinos(id_peca, pinos, tamanho):
    cam = BASE + 'app/assets/pecas.json'
    lista = json.load(io.open(cam, encoding='utf-8'))
    peca = next(p for p in lista if p['id'] == id_peca)
    antigos = {q['nome']: q for q in peca['pinos']}
    novos = []
    for nome, x, y in pinos:
        q = dict(antigos.get(nome) or {'nome': nome, 'sinais': [], 'descricao': ''})
        base = nome.split('.')[0]
        if nome not in antigos:
            q['sinais'] = ['power'] if base in PODER else []
        if base in DESCRICOES and not q.get('descricao'):
            q['descricao'] = DESCRICOES[base]
        q['x'], q['y'] = x, y
        novos.append(q)
    saem = sorted(set(antigos) - {n for n, _, _ in pinos})
    peca['largura'], peca['altura'], peca['pinos'] = tamanho[0], tamanho[1], novos
    io.open(cam, 'w', encoding='utf-8', newline='').write(json.dumps(lista, ensure_ascii=False, indent=1))
    print(f'  pecas.json: caixa {tamanho[0]}x{tamanho[1]}, {len(novos)} pinos'
          + (f'; sairam {saem}' if saem else ''))


GERADORES = {'stm32-bluepill': gerar_bluepill, 'st-nucleo-l031k6': gerar_nucleo32, 'st-nucleo-c031c6': gerar_nucleo64}

if __name__ == '__main__':
    for id_peca in (sys.argv[1:] or list(GERADORES)):
        gravar_pinos(*GERADORES[id_peca]())
