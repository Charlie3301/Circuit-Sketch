"""python ferramentas/extrair-svg/pecas-hc05.py

O modulo Bluetooth HC-05 na placa ZS-040 (a azul de seis pinos, com o botao),
na escala real: 14,8 x 37,2 mm, de pe, com a antena em cima e a barra a 90
graus embaixo (STATE, RXD, TXD, GND, VCC e EN, da esquerda para a direita).

Tudo foi medido na foto de cima de Martyn Currey (martyncurrey.com,
"HC-05_zs-040_01_1200.jpg"), retificada a 40 px/mm pela barra de pinos; a
foto encolhe um pouco na vertical, e o passo de 1,5 mm das ilhas casteladas
do modulo de dentro (a ficha do HC-05: 27 x 13 mm, 34 ilhas) corrige isso.
Do modulo verde: a antena em meandro, o cristal, o BC417 com a gravacao, a
memoria flash com a marca de tinta dourada da fabrica, os passivos, as vias
e o isolamento do plano de cobre (vetorizado da foto). Da placa azul: o LED,
os resistores com o codigo, os capacitores, o regulador SOT-23-5, o diodo, o
botao, as vias e a serigrafia (vetorizada da foto). A foto nao e
reproduzida, e nenhuma marca (o logotipo do fabricante dos chips) e
desenhada.
"""
import importlib.util
import io
import json
import os

AQUI = os.path.dirname(os.path.abspath(__file__))
_spec = importlib.util.spec_from_file_location('placas_arduino', os.path.join(AQUI, 'placas-arduino.py'))
pa = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(pa)
_spec_e = importlib.util.spec_from_file_location('estilo_a25', os.path.join(AQUI, 'estilo_a25.py'))
E = importlib.util.module_from_spec(_spec_e)
_spec_e.loader.exec_module(E)

Desenho, T, f = pa.Desenho, pa.T, pa.f
SERIGRAFIA, ESTANHO, OURO, PRETO_CI = pa.SERIGRAFIA, pa.ESTANHO, pa.OURO, pa.PRETO_CI
A = 96 / 25.4
PASSO = 2.54
TRACADO = json.load(io.open(os.path.join(AQUI, 'modulos-cad', 'tracado-fotos.json'), encoding='utf-8'))['hc-05']
SX, SY = TRACADO['escala']

BW, BH = 14.69 * SX, 36.35 * SY                     # a placa azul (37,2 mm de altura)
X0, Y0 = 0.2, 0.2
L = round((BW + 0.4) * A) / A
H = round((Y0 + BH + 6.2 + 0.15) * A) / A

AZUL, AZUL_BORDA, AZUL_COBRE = '#1a3d8f', '#0f265e', '#2b58b4'
VERDE, VERDE_BORDA, VERDE_VAO, VERDE_ANTENA = '#2b7a50', '#123d27', '#1c5538', '#368c5c'
MARCA_BC = '#8f8a74'


def R(x, y):
    """Da foto retificada (mm medidos) para a peca."""
    return X0 + x * SX, Y0 + y * SY


def texto_girado(d, x, y, t, tam, cor, graus, peso='bold'):
    X, Y = d.t(x, y)
    e = d.t.escala
    d.partes.append(f'<text transform="translate({f(X)} {f(Y)}) rotate({graus})" x="0" y="{f(tam * 0.45 * e)}" '
                    f'font-family="Arial" font-weight="{peso}" font-size="{f(tam * 1.25 * e)}" fill="{cor}" '
                    f'text-anchor="middle">{t}</text>\n')


def poligonos(d, polis, origem, cor):
    ox, oy = origem
    for poli in polis:
        cmds = []
        for anel in poli:
            pts = [R(ox + x, oy + y) for x, y in anel]
            cmds += [('M', *pts[0])] + [('L', *p) for p in pts[1:]] + [('Z',)]
        d.caminho(cmds, cor)
        d.partes[-1] = d.partes[-1].replace('<path ', '<path fill-rule="evenodd" ', 1)


def smd(d, x, y, tam, tipo, vertical=False, codigo=None, giro=0, cor=None):
    cx, cy = R(x, y)
    E.passivo(d, cx, cy, tam, tipo, vertical=vertical, cor=cor)
    if codigo:
        texto_girado(d, cx, cy, codigo, 0.55 if tam == '0805' else 0.42, '#e8e8e8', giro, peso='normal')


def gerar():
    d = Desenho(T(A, 0, 0, A, 0, 0))

    # ---- a placa azul ----
    E.sombra(d, X0, Y0, BW, BH, rx=0.35, op=0.35)
    d.ret(X0, Y0, BW, BH, AZUL, rx=0.35, stroke=AZUL_BORDA, sw=0.15)
    x0, y0 = R(4.44, 27.06)
    x1, y1 = R(8.13, 28.56)
    d.ret(x0, y0, x1 - x0, y1 - y0, AZUL_COBRE, rx=0.2)
    d.ret(X0, Y0, BW, BH, 'url(#luz)', rx=0.35)

    # as ilhas da placa azul debaixo das do modulo (soldadas pela borda castelada)
    ys_lado = [7.1 + 1.465 * k for k in range(13)]
    xs_baixo = [2.16 + 1.484 * k for k in range(8)]
    for y in ys_lado:
        for xa, xb in ((0.2, 1.62), (13.55, 14.62)):
            p0, p1 = R(xa, y - 0.38), R(xb, y + 0.38)
            d.ret(p0[0], p0[1], p1[0] - p0[0], p1[1] - p0[1], ESTANHO, rx=0.08)
    for x in xs_baixo:
        p0, p1 = R(x - 0.4, 26.0), R(x + 0.4, 27.05)
        d.ret(p0[0], p0[1], p1[0] - p0[0], p1[1] - p0[1], ESTANHO, rx=0.08)

    # ---- o modulo verde (o HC-05 de verdade, 13 x 27 mm, castelado) ----
    nx0, ny0 = R(1.13, 0.13)
    nx1, ny1 = R(14.0, 26.4)
    E.sombra(d, nx0, ny0, nx1 - nx0, ny1 - ny0, rx=0.15, op=0.35)
    d.ret(nx0, ny0, nx1 - nx0, ny1 - ny0, VERDE, rx=0.15, stroke=VERDE_BORDA, sw=0.1)
    ka = R(1.13, 4.62)
    d.ret(nx0, ny0, nx1 - nx0, ka[1] - ny0, VERDE_ANTENA, rx=0.15)                     # sem cobre debaixo da antena
    poligonos(d, TRACADO['vaos_do_nucleo'], TRACADO['origem_dos_vaos'], VERDE_VAO)
    d.ret(nx0, ny0, nx1 - nx0, ny1 - ny0, 'url(#luz)', rx=0.15)
    # a antena em meandro (o cobre dourado, sem mascara)
    meandro = [(1.5, 0.81), (2.28, 0.81), (2.28, 3.19), (3.56, 3.19), (3.56, 0.81), (4.84, 0.81), (4.84, 3.19),
               (6.15, 3.19), (6.15, 0.81), (7.46, 0.81), (7.46, 3.19), (8.78, 3.19), (8.78, 0.81), (10.06, 0.81),
               (10.06, 3.19), (11.4, 3.19), (11.4, 0.81), (12.69, 0.81), (12.69, 4.05)]
    d.poli([R(x, y) for x, y in meandro], None, stroke=OURO, sw=0.6, fechado=False, cap='round')
    d.partes[-1] = d.partes[-1].replace('<path ', '<path stroke-linejoin="round" ', 1)
    d.poli([R(x, y) for x, y in meandro], None, stroke='#fff3c4', sw=0.18, fechado=False, cap='round', op=0.35)
    # as ilhas casteladas: 13 de cada lado e 8 embaixo, a 1,5 mm
    for y in ys_lado:
        E.ilha_castelada(d, *R(1.13, y), 'esq', comp=0.7, larg=0.85, furo=0.55)
        E.ilha_castelada(d, *R(14.0, y), 'dir', comp=0.7, larg=0.85, furo=0.55)
    for x in xs_baixo:
        E.ilha_castelada(d, *R(x, 26.4), 'baixo', comp=0.7, larg=0.85, furo=0.55)
    # as vias e os pontos de teste
    for x, y in ((4.67, 5.0), (5.5, 5.0), (6.5, 5.0), (7.5, 5.0), (8.46, 5.0), (9.5, 5.0), (10.42, 5.0), (11.37, 5.0),
                 (4.67, 6.05), (8.37, 6.05), (9.29, 6.05), (10.42, 6.05), (7.92, 7.63), (10.67, 8.05), (11.33, 8.05)):
        d.circ(*R(x, y), 0.07, '#11331f')
    for x, y, r in ((2.25, 5.5, 0.22), (11.94, 9.85, 0.22), (4.19, 19.92, 0.2)):
        d.circ(*R(x, y), r, OURO)
        d.circ(*R(x, y), r, 'url(#luz)', op=0.6)
    d.circ(*R(9.81, 7.0), 0.33, OURO)
    d.circ(*R(9.81, 7.0), 0.15, '#0c1016')

    # os passivos do modulo
    for x, y, tam, tipo, vert, cor in ((3.75, 6.5, '0805', 'C', True, '#cfc8bd'), (6.13, 5.75, '0603', 'C', False, '#d6d1c6'),
                                       (6.4, 7.05, '0603', 'C', False, '#b9c9b0'), (12.0, 5.81, '0603', 'C', False, '#d6d1c6'),
                                       (12.0, 7.0, '0603', 'C', False, '#d6d1c6'), (3.25, 8.63, '0603', 'C', False, '#d6d1c6'),
                                       (5.56, 8.63, '0603', 'C', True, None), (12.0, 8.81, '0603', 'C', False, None),
                                       (3.25, 14.22, '0603', 'C', False, '#d6d1c6'), (3.09, 17.19, '0805', 'C', True, None),
                                       (5.13, 18.75, '0603', 'C', False, None), (11.14, 18.67, '0805', 'C', False, '#d6d1c6'),
                                       (2.86, 22.73, '0603', 'C', False, None), (2.86, 23.75, '0603', 'C', False, None)):
        smd(d, x, y, tam, tipo, vertical=vert, cor=cor)
    for x, y in ((5.3, 7.05), (7.75, 18.83), (8.9, 18.83)):
        smd(d, x, y, '0402', 'R')
    smd(d, 2.94, 21.72, '0603', 'R')
    for x in (2.31, 3.33):                                                              # o lugar vazio de um 0603
        d.ret_c(*R(x, 24.92), 0.62, 0.72, ESTANHO, rx=0.06)
    # o balun branco (seis pernas) do radio
    bx, by = R(9.29, 8.88)
    for x in (8.67, 9.29, 9.92):
        for y in (8.1, 9.85):
            d.ret_c(*R(x, y), 0.36, 0.5, ESTANHO, rx=0.05)
    for x in (8.04, 10.52):
        d.ret_c(*R(x, 8.88), 0.4, 0.6, ESTANHO, rx=0.05)
    E.sombra(d, bx - 0.98, by - 0.6, 1.96, 1.2)
    d.ret_c(bx, by, 1.96, 1.2, 'url(#ceramica)', rx=0.08, stroke='#b3ab96', sw=0.05)
    d.texto(bx - 0.2, by, '41', 0.5, '#5a5a5a', alinhar='center', peso='normal')
    d.circ(bx + 0.45, by, 0.12, '#6a6a6a')
    # o cristal (3,2 x 2,5 mm, de pe)
    for x in (2.55, 4.2):
        for y in (9.75, 13.22):
            d.ret_c(*R(x, y), 0.72, 0.55, ESTANHO, rx=0.06)
    c0, c1 = R(2.19, 10.06), R(4.56, 12.97)
    E.sombra(d, c0[0], c0[1], c1[0] - c0[0], c1[1] - c0[1], rx=0.25)
    d.ret(c0[0], c0[1], c1[0] - c0[0], c1[1] - c0[1], '#d9cfae', rx=0.25, stroke='#9c8f62', sw=0.08)
    d.ret(c0[0] + 0.25, c0[1] + 0.25, c1[0] - c0[0] - 0.5, c1[1] - c0[1] - 0.5, '#cfc49e', rx=0.18)
    d.ret(c0[0], c0[1], c1[0] - c0[0], c1[1] - c0[1], 'url(#luz)', rx=0.25)
    # o BC417 (8 x 8,2 mm) com a gravacao e a marca do canto A1
    b0, b1 = R(4.95, 9.95), R(12.9, 18.0)
    E.sombra(d, b0[0], b0[1], b1[0] - b0[0], b1[1] - b0[1], rx=0.15)
    d.ret(b0[0], b0[1], b1[0] - b0[0], b1[1] - b0[1], PRETO_CI, rx=0.15)
    d.ret(b0[0], b0[1], b1[0] - b0[0], b1[1] - b0[1], 'url(#luz)', rx=0.15)
    for t, x in (('BC417', 8.55), ('143BC0', 7.39), ('K435BM', 6.22)):
        texto_girado(d, *R(x, 14.22), t, 0.74, MARCA_BC, 90, peso='normal')
    d.ret_c(*R(11.98, 11.15), 0.72, 0.72, '#35373b', rx=0.05)
    # a memoria flash, com a marca de tinta dourada por cima da gravacao
    m0, m1 = R(4.85, 19.63), R(12.79, 25.51)
    E.sombra(d, m0[0], m0[1], m1[0] - m0[0], m1[1] - m0[1], rx=0.15)
    d.ret(m0[0], m0[1], m1[0] - m0[0], m1[1] - m0[1], PRETO_CI, rx=0.15)
    d.ret(m0[0], m0[1], m1[0] - m0[0], m1[1] - m0[1], 'url(#luz)', rx=0.15)
    for t, x, y in (('919', 11.35, 21.7), ('I-70G', 11.22, 22.44), ('Z', 5.67, 22.44), ('3R', 5.97, 23.13), ('TAI', 6.1, 23.81)):
        d.texto(*R(x, y), t, 0.5, '#6c6e73', alinhar='center', peso='normal')
    d.circ(*R(5.69, 24.56), 0.13, '#3a3c40')
    tinta = [(85, 165), (95, 140), (130, 135), (170, 150), (205, 190), (230, 250), (260, 300), (295, 345), (360, 300),
             (430, 250), (500, 200), (560, 160), (610, 125), (630, 110), (640, 130), (660, 118), (685, 125), (680, 160),
             (650, 210), (600, 280), (540, 350), (470, 420), (400, 470), (330, 505), (280, 510), (240, 490), (200, 440),
             (165, 380), (130, 300), (100, 230), (90, 195)]

    def da_foto(cx, cy):                                         # o recorte ampliado 4x a partir de (690, 640) da foto
        return R((690 + cx / 4 - 588) / 23.62, (640 + cy / 4 - 200) / 23.62)
    d.poli([da_foto(*p) for p in tinta], '#cfae5a', stroke='#a8873a', sw=0.06, op=0.95)
    d.poli([da_foto(*p) for p in tinta], 'url(#luz)', op=0.5)

    # ---- as pecas da placa azul ----
    poligonos(d, TRACADO['serigrafia_da_base'], TRACADO['origem_da_serigrafia'], SERIGRAFIA)
    lx, ly = R(0.78, 28.7)
    E.led_smd(d, lx, ly, '#ff7a6e', '0805', vertical=True)
    for x, y, cod in ((2.69, 29.35, '202'), (6.13, 29.35, '102'), (9.38, 29.35, '224'), (9.5, 28.06, '103')):
        smd(d, x, y, '0805', 'R', codigo=cod, giro=180)
    for x in (0.81, 2.25, 7.88, 9.38):
        smd(d, x, 31.56, '0603', 'C', vertical=True)
    # o regulador (SOT-23-5, de pe: duas pernas a esquerda, tres a direita)
    sx, sy = R(5.13, 31.5)
    for y, lado in ((30.55, -1), (32.45, -1), (30.55, 1), (31.5, 1), (32.45, 1)):
        px_ = R(5.13 + lado * 1.37, y)
        d.ret_c(*px_, 0.9, 0.55, ESTANHO, rx=0.06)
        d.ret(sx + (0.8 if lado > 0 else -1.3), px_[1] - 0.19, 0.5, 0.38, 'url(#metal)', rx=0.04)
    E.sombra(d, sx - 0.8, sy - 1.45, 1.6, 2.9, rx=0.12)
    d.ret_c(sx, sy, 1.6, 2.9, PRETO_CI, rx=0.12)
    d.ret_c(sx, sy, 1.6, 2.9, 'url(#luz)', rx=0.12)
    # o diodo (SOD-123, de pe, a faixa do catodo em cima)
    dx_, dy_ = R(10.95, 30.88)
    for y in (29.25, 32.45):
        d.ret_c(*R(10.95, y), 1.0, 0.75, ESTANHO, rx=0.08)
    E.sombra(d, dx_ - 0.75, dy_ - 1.3, 1.5, 2.6, rx=0.15)
    d.ret_c(dx_, dy_, 1.5, 2.6, '#202124', rx=0.15)
    d.ret_c(dx_, dy_ - 0.95, 1.5, 0.35, '#5d6066')
    d.ret_c(dx_, dy_, 1.5, 2.6, 'url(#luz)', rx=0.15)
    # o botao (a chave de 3 x 4 mm: o botao preto no corpo de metal)
    for y0_, y1_ in ((27.5, 28.06), (32.4, 33.0)):
        p0, p1 = R(13.1, y0_), R(14.3, y1_)
        d.ret(p0[0], p0[1], p1[0] - p0[0], p1[1] - p0[1], ESTANHO, rx=0.08)
    k0, k1 = R(12.5, 28.44), R(14.6, 32.19)
    E.sombra(d, k0[0], k0[1], k1[0] - k0[0], k1[1] - k0[1], rx=0.2)
    d.ret(k0[0], k0[1], k1[0] - k0[0], k1[1] - k0[1], 'url(#metal)', rx=0.2, stroke='#7c838b', sw=0.08)
    d.ret(k0[0] + 0.25, k0[1] + 0.25, k1[0] - k0[0] - 0.5, k1[1] - k0[1] - 0.5, None, rx=0.15, stroke='#e9ecef', sw=0.08)
    bxy = R(13.69, 30.31)
    d.circ(bxy[0] + 0.08, bxy[1] + 0.1, 0.78, '#000000', op=0.35)
    d.circ(*bxy, 0.78, '#26282c', stroke='#0b0c0d', sw=0.06)
    d.circ(*bxy, 0.78, 'url(#cupula)', op=0.5)
    # as vias da placa azul
    for x, y, r in ((7.44, 28.06, 0.3), (1.5, 31.06, 0.25)):
        d.circ(*R(x, y), r, AZUL_COBRE)
        d.circ(*R(x, y), r * 0.5, '#0c1430')
    d.circ(*R(10.0, 33.6), 0.2, '#0c0d0f')

    # ---- a barra a 90 graus (os pinos saindo por baixo, no plano da placa) ----
    xs = [X0 + 1.15 * SX + k * PASSO for k in range(6)]
    xs = [x - (xs[0] + xs[-1]) / 2 + X0 + 7.5 * SX for x in xs]
    y_borda = Y0 + BH
    alto = 2.85
    E.sombra(d, min(xs) - 1.27, y_borda - alto, max(xs) - min(xs) + 2.54, alto, rx=0.15)
    d.ret(min(xs) - 1.27, y_borda - alto, max(xs) - min(xs) + 2.54, alto, 'url(#plastico)', rx=0.15, stroke='#0b0c0d', sw=0.1)
    comp = H - 0.15 - y_borda
    for x in xs:
        d.ret(x - 1.15, y_borda - alto + 0.2, 2.3, alto - 0.4, None, rx=0.12, stroke='#2a2c30', sw=0.08)
        d.ret(x - 0.32, y_borda - 1.4, 0.64, comp + 1.4, 'url(#metal-h)', stroke='#80868d', sw=0.05)
        d.ret(x - 0.32, y_borda + comp - 0.5, 0.64, 0.5, '#e9ecef')
    io.open(pa.BASE + 'app/assets/hc-05.svg', 'w', encoding='utf-8', newline='').write(E.svg(d, L, H))

    nomes = ['STATE', 'RXD', 'TXD', 'GND', 'VCC', 'EN']
    descricoes = {
        'STATE': ([], 'Estado: HIGH com um aparelho conectado'),
        'RXD': ([], 'Recebe da placa: ligue no TX dela (a entrada e de 3,3 V; no Uno, use um divisor de tensao)'),
        'TXD': ([], 'Manda para a placa: ligue no RX dela'),
        'GND': (['power'], 'GND'),
        'VCC': (['power'], 'Alimentacao: 3,6 a 6 V (o regulador da placa baixa para 3,3 V)'),
        'EN': ([], 'Em HIGH ao ligar (ou com o botao apertado): o modo de comandos AT, a 38400 baud'),
    }
    peca = {'id': 'hc-05', 'largura': round(L * A), 'altura': round(H * A),
            'pinos': [{'nome': n, 'x': round(x * A, 2), 'y': round((H - 0.9) * A, 2), 'sinais': descricoes[n][0],
                       'descricao': descricoes[n][1]} for n, x in zip(nomes, xs)],
            'variantes': [], 'metades': False}
    cam = pa.BASE + 'app/assets/pecas.json'
    dados = [p for p in json.load(io.open(cam, encoding='utf-8')) if p['id'] != 'hc-05'] + [peca]
    dados.sort(key=lambda p: p['id'])
    io.open(cam, 'w', encoding='utf-8', newline='').write(json.dumps(dados, ensure_ascii=False, indent=1))
    print(f"hc-05: {peca['largura']}x{peca['altura']} px, {len(nomes)} pinos")
    print(f'luz (EditorModulos.cs): hc-05: ({lx * A:.2f}, {ly * A:.2f}, 0.5 * Mm, vermelho, "hc05")')
    print(f'botao (EditorCircuito.BotoesPorPlaca): hc-05: ({bxy[0] * A:.2f}, {bxy[1] * A:.2f}, 4.5, "KEY")')


gerar()
