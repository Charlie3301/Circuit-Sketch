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
  - hx711                 : o modulo verde comum (XFW-HX711, 24,2 x 15,7 mm): os
                            seis furos da celula (E+, E-, A-, A+, B-, B+) numa
                            ponta e o GND, DT, SCK, VCC na outra.

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
_spec_e = importlib.util.spec_from_file_location('estilo_a25', os.path.join(AQUI, 'estilo_a25.py'))
E = importlib.util.module_from_spec(_spec_e)
_spec_e.loader.exec_module(E)
mover, rodar = pa.mover, pa.rodar
SERIGRAFIA, MARCA_CI, PRETO_CI = pa.SERIGRAFIA, pa.MARCA_CI, pa.PRETO_CI
_spec_k = importlib.util.spec_from_file_location('keyes_a25', os.path.join(AQUI, 'keyes_a25.py'))
KEYES = importlib.util.module_from_spec(_spec_k)
_spec_k.loader.exec_module(KEYES)
TRACADO = json.load(io.open(os.path.join(AQUI, 'modulos-cad', 'tracado-fotos.json'), encoding='utf-8'))
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
  <radialGradient id="latao" cx="0.38" cy="0.32" r="0.8"><stop offset="0" stop-color="#fbe9a8"/><stop offset="0.55" stop-color="#d4ab52"/><stop offset="1" stop-color="#93702a"/></radialGradient>
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


# ---- o padrao das placas (Alpha 25): o cobre, as trilhas, as vias e as pecas com marcacao ----
def cobre_da_foto(d, chave, x0, y0, cor):
    """O cobre de cima vetorizado da foto retificada (modulos-cad/tracado-fotos.json)."""
    for poli in TRACADO[chave]['cobre']:
        cmds = []
        for anel in poli:
            cmds += [('M', x0 + anel[0][0], y0 + anel[0][1])] + [('L', x0 + x, y0 + y) for x, y in anel[1:]] + [('Z',)]
        d.caminho(cmds, cor)
        d.partes[-1] = d.partes[-1].replace('<path ', '<path fill-rule="evenodd" ', 1)


def trilha(d, pts, cor, larg=0.32):
    """Uma trilha medida na foto (as que a vetorizacao nao pegou inteiras)."""
    d.poli(pts, None, stroke=cor, sw=larg, fechado=False, cap='round')
    d.partes[-1] = d.partes[-1].replace('<path ', '<path stroke-linejoin="round" ', 1)


def via(d, x, y, cor, fundo, r=0.32):
    d.circ(x, y, r, cor)
    d.circ(x, y, r * 0.5, fundo)


def furo_metalizado(d, x, y, r_anel=0.78, r_furo=0.36):
    d.circ(x, y, r_anel, ESTANHO)
    d.circ(x, y, r_anel, 'url(#luz)')
    d.circ(x, y, r_anel * 0.93, None, '#ffffff', 0.08, op=0.35)
    d.circ(x, y, r_furo, '#0c0d0f')


def texto_girado(d, x, y, t, tam, cor, graus, peso='bold'):
    """Texto centrado em (x, y), girado de verdade (o d.texto deixa sempre legivel)."""
    X, Y = d.t(x, y)
    e = d.t.escala
    d.partes.append(f'<text transform="translate({f(X)} {f(Y)}) rotate({graus})" x="0" y="{f(tam * 0.45 * e)}" '
                    f'font-family="Arial" font-weight="{peso}" font-size="{f(tam * 1.25 * e)}" fill="{cor}" '
                    f'text-anchor="middle">{t}</text>\n')


def trimpot_3296(d, cx, cy, w=9.5, h=4.8, parafuso=-1, marca=('W 104', 'T 110')):
    """O trimpot azul 3296W de pe, visto de cima: o corpo, a borda do molde, a marcacao e o
    parafuso de latao com a fenda numa das pontas (parafuso: -1 a esquerda, 1 a direita)."""
    E.sombra(d, cx - w / 2, cy - h / 2, w, h, rx=0.3, op=0.4)
    d.ret_c(cx, cy, w, h, '#2b63c8', rx=0.3, stroke='#173d80', sw=0.12)
    d.ret_c(cx, cy, w - 0.7, h - 0.7, None, rx=0.2, stroke='#4c84e0', sw=0.1)
    d.ret_c(cx, cy, w, h, 'url(#luz)', rx=0.3)
    px = cx + parafuso * (w / 2 - 1.55)
    for k, t in enumerate(marca):
        texto_girado(d, cx - parafuso * 1.2, cy + (k - 0.5) * 1.25, t, 0.75, '#163a7c', 0)
    d.circ(px + 0.1, cy + 0.12, 1.3, '#000000', op=0.35)
    d.circ(px, cy, 1.3, 'url(#latao)', stroke='#7d6420', sw=0.1)
    d.linha(px - 1.0, cy, px + 1.0, cy, '#6b5418', 0.32)
    d.circ(px - 0.35, cy - 0.4, 0.3, '#fff4c8', op=0.7)


def caixa_silk(d, cx, cy, w, h, larg=0.15):
    """O contorno da serigrafia em volta de um passivo (as pontas arredondadas, como nas placas)."""
    d.ret_c(cx, cy, w, h, None, rx=min(w, h) * 0.3, stroke=SERIGRAFIA, sw=larg)


def led_1206(d, cx, cy, vertical=False):
    """O LED 1206 apagado: o corpo branco leitoso, o chip e a marca verde do catodo."""
    w, h = (1.6, 3.2) if vertical else (3.2, 1.6)
    E.sombra(d, cx - w / 2, cy - h / 2, w, h)
    for s_ in (-1, 1):
        if vertical:
            d.ret_c(cx, cy + s_ * 1.25, 1.5, 0.7, ESTANHO, rx=0.08)
        else:
            d.ret_c(cx + s_ * 1.25, cy, 0.7, 1.5, ESTANHO, rx=0.08)
    d.ret_c(cx, cy, w * (1 if vertical else 0.72), h * (0.72 if vertical else 1), '#f2efe6', rx=0.12)
    d.ret_c(cx, cy, 0.7, 0.7, '#e8d9c6', rx=0.1)
    d.ret_c(cx, cy, 0.28, 0.28, '#b79e76')
    if vertical:
        d.ret_c(cx, cy - h * 0.3, w * 0.9, 0.22, '#2fa37a', op=0.85)
    else:
        d.ret_c(cx - w * 0.3, cy, 0.22, h * 0.9, '#2fa37a', op=0.85)
    d.ret_c(cx, cy, w, h, 'url(#luz)', rx=0.12)


def sop8(d, cx, cy, linhas, corpo=(5.0, 5.1), passo=1.27, distancia=6.55, giro_texto=90):
    """O LM393 em SOP-8 de pe (quatro pernas de cada lado, na esquerda e na direita)."""
    w, h = corpo
    for k in range(4):
        y = cy + (k - 1.5) * passo
        for s_ in (-1, 1):
            d.ret_c(cx + s_ * distancia / 2, cy + (k - 1.5) * passo, 1.3, 0.62, ESTANHO, rx=0.06)
            d.ret(cx + s_ * w / 2 - (0 if s_ > 0 else 0.9), y - 0.21, 0.9, 0.42, 'url(#metal)', rx=0.05)
    E.sombra(d, cx - w / 2, cy - h / 2, w, h, rx=0.2)
    d.ret_c(cx, cy, w, h, PRETO_CI, rx=0.2)
    d.ret_c(cx, cy, w, h, 'url(#luz)', rx=0.2)
    d.circ(cx - w / 2 + 0.7, cy + h / 2 - 0.7, 0.3, '#3a3c41')
    n = len(linhas)
    for k, (t, tam) in enumerate(linhas):
        o = (k - (n - 1) / 2) * 1.0
        d.entrar(mover(cx + o, cy) * rodar(giro_texto))
        d.texto(0, 0, t, tam, MARCA_CI, alinhar='center', peso='normal')
        d.sair()


def trimpot_3362(d, cx, cy, w=7.0, h=7.6):
    """O trimpot azul 3362 de uma volta, de cima: o corpo quadrado, o rotor com a fenda em cruz e a
    marca do pino 1."""
    E.sombra(d, cx - w / 2, cy - h / 2, w, h, rx=0.3, op=0.35)
    d.ret_c(cx, cy, w, h, '#2f63c9', rx=0.3, stroke='#17408c', sw=0.12)
    d.ret_c(cx, cy, w - 0.8, h - 0.8, None, rx=0.25, stroke='#4d81e0', sw=0.1)
    d.ret_c(cx, cy, w, h, 'url(#luz)', rx=0.3)
    for k in range(16):                                             # as nervuras em volta do rotor
        a = math.radians(k * 22.5)
        d.linha(cx + 2.55 * math.cos(a), cy + 2.55 * math.sin(a), cx + 2.95 * math.cos(a), cy + 2.95 * math.sin(a),
                '#1f4fa8', 0.22)
    d.circ(cx, cy, 2.3, '#2a5bbb', stroke='#183f88', sw=0.1)
    d.circ(cx, cy, 1.75, '#c9ced4', stroke='#7c838b', sw=0.08)
    d.ret_c(cx, cy, 2.9, 0.62, '#2b2e33', rx=0.12)
    d.ret_c(cx, cy, 0.62, 2.9, '#2b2e33', rx=0.12)
    d.circ(cx, cy, 2.3, 'url(#cupula)', op=0.4)
    d.circ(cx + w / 2 - 1.0, cy - h / 2 + 1.5, 0.32, '#c9ced4', stroke='#2a2d31', sw=0.08)
    d.texto(cx, cy - h / 2 + 0.95, '3362', 0.62, '#173d82', ang=180, alinhar='center', peso='bold')


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


def modulo_lm393(nome, sensor, nomes, descricoes, rotulos_sensor, X0):
    """A placa azul de 32 x 14 mm do LM393 ("MH-Sensor-Series"), medida na foto
    de cima do modulo do LDR (a do sensor de chama e a mesma placa): o sensor
    deitado para fora da ponta, a coluna de passivos 1206, o trimpot 3362, o
    LM393 em SOP-8, os 102 e os LEDs PWR e DO, o furo de 3 mm e a barra de
    quatro pinos a 90 graus. O cobre vem da foto (vetorizado), mais as trilhas
    medidas que a vetorizacao nao pegou inteiras."""
    L, H = caixa(X0 + 32.0 + 6.2, 14.4)
    Y0 = (H - 14.0) / 2
    cy = H / 2
    ys = [cy + (k - 1.5) * PASSO for k in range(4)]
    mascara, borda, cor_cobre = '#163d84', '#0b2552', '#3570cc'
    d = Desenho(T(A, 0, 0, A, 0, 0))
    if sensor is receptor_ir:
        sensor(d, X0, Y0)                                               # deitado para fora, por baixo da ponta
    E.sombra(d, X0, Y0, 32.0, 14.0, rx=0.5, op=0.35)
    d.ret(X0, Y0, 32.0, 14.0, mascara, rx=0.5, stroke=borda, sw=0.15)
    cobre_da_foto(d, 'ldr', X0, Y0, cor_cobre)
    for pts in ([(2.05, 2.6), (2.3, 4.6)], [(2.3, 9.5), (2.05, 11.3)], [(6.35, 2.3), (6.35, 3.4)],
                [(6.35, 4.8), (6.35, 5.8)], [(3.9, 5.9), (5.3, 7.15), (8.9, 7.15), (9.3, 6.7)],
                [(9.2, 4.05), (12.9, 4.05)], [(9.3, 9.15), (13.2, 9.15)], [(9.3, 11.75), (13.2, 11.75)],
                [(6.3, 12.2), (6.6, 13.0), (13.2, 13.0)], [(4.95, 9.95), (6.15, 9.95), (6.15, 9.5)],
                [(21.4, 2.9), (21.4, 2.1), (26.7, 2.1), (28.3, 3.0)], [(21.9, 3.95), (23.8, 3.95)],
                [(21.25, 7.95), (21.4, 9.1)], [(21.9, 11.25), (23.8, 11.25)], [(26.4, 3.95), (27.2, 3.95)],
                [(26.4, 11.25), (27.0, 11.25), (28.3, 10.8)], [(19.8, 9.2), (20.6, 9.2), (20.6, 8.0)]):
        trilha(d, [(X0 + x, Y0 + y) for x, y in pts], cor_cobre)
    for x, y in ((6.35, 2.3), (4.95, 9.95), (10.8, 8.85), (21.25, 7.95)):
        via(d, X0 + x, Y0 + y, cor_cobre, borda)
    d.ret(X0, Y0, 32.0, 14.0, 'url(#luz)', rx=0.5)
    # a serigrafia: o octogono das ilhas do sensor, os contornos, os rotulos
    oct_ = [(1.4, 4.3), (3.3, 4.3), (4.0, 5.0), (4.0, 6.6), (3.6, 7.1), (4.0, 7.6), (4.0, 9.3), (3.3, 10.0),
            (1.4, 10.0), (0.7, 9.3), (0.7, 7.6), (1.1, 7.1), (0.7, 6.6), (0.7, 5.0)]
    d.poli([(X0 + x, Y0 + y) for x, y in oct_], None, stroke=SERIGRAFIA, sw=0.18)
    for t, y in zip(rotulos_sensor, (3.3, 10.9)):
        if t:
            d.texto(X0 + 4.6, Y0 + y, t, 1.0, SERIGRAFIA, alinhar='center', peso='bold')
    for y in (4.05, 6.55, 9.15, 11.6):
        caixa_silk(d, X0 + 7.6, Y0 + y, 4.4, 2.0)
    for y in (4.05, 10.45):
        caixa_silk(d, X0 + 21.4, Y0 + y, 2.0, 3.6)
    for y in (3.9, 11.3):
        d.ret_c(X0 + 25.1, Y0 + y, 3.6, 2.0, None, rx=0.2, stroke=SERIGRAFIA, sw=0.15)
    d.texto(X0 + 25.1, Y0 + 1.0, 'PWR-LED', 0.95, SERIGRAFIA, alinhar='center', peso='bold')
    d.texto(X0 + 25.0, Y0 + 13.25, 'DO-LED', 0.95, SERIGRAFIA, alinhar='center', peso='bold')
    for t, y in zip(('VCC', 'GND', 'DO', 'AO'), ys):
        d.texto(X0 + 27.4, y, t, 0.82, SERIGRAFIA, ang=90, alinhar='center', peso='bold')
    # os furos: os dois metalizados da ponta e o de 3 mm de fixacao
    furo_metalizado(d, X0 + 2.05, Y0 + 1.85)
    furo_metalizado(d, X0 + 2.05, Y0 + 12.05)
    d.circ(X0 + 25.0, Y0 + 7.0, 1.55, '#0c0d0f')
    d.circ(X0 + 25.0, Y0 + 7.0, 1.55, None, '#0a1f45', 0.15)
    # as ilhas do sensor (as pernas ja vem soldadas, por cima)
    for y in (5.65, 8.55):
        furo_metalizado(d, X0 + 2.3, Y0 + y, 0.85, 0.42)
    if sensor is ldr:
        sensor(d, X0, Y0)                                               # em pe sobre as ilhas
    # as pecas
    E.passivo(d, X0 + 7.6, Y0 + 4.05, '1206', 'C')
    E.passivo(d, X0 + 7.6, Y0 + 6.55, '1206', 'C')
    E.passivo(d, X0 + 7.6, Y0 + 9.15, '1206', 'R', '103')
    E.passivo(d, X0 + 7.6, Y0 + 11.6, '1206', 'R', '103')
    E.passivo(d, X0 + 21.4, Y0 + 4.05, '1206', 'R', '102', vertical=True)
    E.passivo(d, X0 + 21.4, Y0 + 10.45, '1206', 'R', '102', vertical=True)
    led_1206(d, X0 + 25.1, Y0 + 3.9)
    led_1206(d, X0 + 25.1, Y0 + 11.3)
    sop8(d, X0 + 16.5, Y0 + 11.1, [('LM393', 0.9), ('98M', 0.75)])
    trimpot_3362(d, X0 + 16.55, Y0 + 3.95)
    barra_90(d, X0 + 32.0, ys, L - 0.15 - (X0 + 32.0))
    gravar(nome, d, L, H)
    guardar(nome, L, H, [(n, L - 0.9, y, s, dsc) for n, y, (s, dsc) in zip(nomes, ys, descricoes)])
    luz(nome, X0 + 25.1, Y0 + 3.9, 0.55, 'vermelho', 'vcc-gnd')
    luz(nome, X0 + 25.1, Y0 + 11.3, 0.55, 'vermelho', 'sensor-do')


def pernas(d, x_pad, ys, x_fim):
    """As duas pernas do sensor, das ilhas ate o corpo deitado fora da placa."""
    for y in ys:
        d.linha(x_pad, y, x_fim, y, '#7d848b', 0.62)
        d.linha(x_pad, y, x_fim, y, '#d3d8dd', 0.42)
        d.circ(x_pad, y, 0.55, ESTANHO)
        d.circ(x_pad - 0.12, y - 0.12, 0.2, '#ffffff', op=0.7)


def ldr(d, x0, y0):
    """O LDR de 5 mm em pe sobre as duas ilhas da ponta, visto de cima: a ceramica e a trilha de CdS."""
    cx, cy = x0 + 2.6, y0 + 7.1
    d.circ(cx + 0.2, cy + 0.25, 2.6, '#000000', op=0.3)
    d.circ(cx, cy, 2.6, '#c9653f', stroke='#7c3a20', sw=0.12)                         # a borda de epoxi
    d.circ(cx, cy, 2.25, '#efe4cc')
    pts = []
    for k in range(7):
        y = cy - 1.6 + k * 0.53
        x_a, x_b = cx - 1.75, cx + 1.75
        pts += [(x_a, y), (x_b, y)] if k % 2 == 0 else [(x_b, y), (x_a, y)]
    d.poli(pts, None, stroke='#b5452e', sw=0.3, fechado=False)
    for s_ in (-1, 1):
        d.caminho([('M', cx - 1.95, cy + s_ * 1.0), ('A', 2.25, 0, 1 if s_ < 0 else 0, cx + 1.95, cy + s_ * 1.0),
                   ('Z',)], '#d7c39a')
    d.circ(cx, cy, 2.25, 'url(#cupula)', op=0.6)


def receptor_ir(d, x0, y0):
    """O fotodiodo infravermelho preto de 5 mm, deitado para fora da ponta."""
    cy = y0 + 7.1
    pernas(d, x0 + 2.3, (y0 + 5.65, y0 + 8.55), x0 - 1.0)
    d.ret(x0 - 1.4, cy - 2.95, 1.0, 5.9, '#121315', rx=0.2)                                # o colar
    d.caminho([('M', x0 - 1.4, cy - 2.5), ('L', x0 - 4.9, cy - 2.5), ('A', 2.5, 0, 0, x0 - 4.9, cy + 2.5),
               ('L', x0 - 1.4, cy + 2.5), ('Z',)], '#18191c', stroke='#050506', sw=0.1)
    d.ret(x0 - 5.6, cy - 1.9, 4.2, 0.7, '#ffffff', rx=0.3, op=0.18)


desc_dig = 'Saida digital do comparador: muda quando passa do ponto do trimpot (o LED DO acende)'
modulo_lm393('photoresistor-sensor', ldr, ['VCC', 'GND', 'DO', 'AO'],
             [(['power'], 'Alimentacao: 3,3 a 5 V'), (['power'], 'GND'),
              ([], 'Saida digital: LOW com a luz acima do ponto do trimpot (o LED DO acende)'),
              (['analog'], 'Saida analogica: mais luz, menos tensao')], ('', ''), 0.2)
modulo_lm393('flame-sensor', receptor_ir, ['VCC', 'GND', 'DOUT', 'AOUT'],
             [(['power'], 'Alimentacao: 3,3 a 5 V'), (['power'], 'GND'),
              ([], 'Saida digital: LOW com chama (o LED DO acende)'),
              (['analog'], 'Saida analogica: mais chama, menos tensao')], ('+', '-'), 9.4)

# =====================================================================
#  ir-obstacle-sensor: o modulo de obstaculo infravermelho (FC-51, a placa MH-B)
# =====================================================================


def mover_cmds(cmds, dx, dy):
    """Os comandos de um caminho deslocados (a sombra)."""
    saida = []
    for c in cmds:
        if c[0] in 'ML':
            saida.append((c[0], c[1] + dx, c[2] + dy))
        elif c[0] == 'A':
            saida.append(('A', c[1], c[2], c[3], c[4] + dx, c[5] + dy))
        else:
            saida.append(c)
    return saida


def led5_deitado(d, x_base, cy, x_ponta, transparente):
    """O LED de 5 mm deitado para fora da placa, visto de cima: o colar junto da
    placa (que termina em x_base), o corpo de 5 mm e a ponta redonda em x_ponta.
    O receptor e de epoxi preto; o emissor infravermelho, de epoxi transparente,
    com a moldura das pernas (o catodo largo, o anodo fino e o fio) por dentro."""
    r = 2.5
    x_colar = x_base - 1.0
    x_cupula = x_ponta + r
    corpo = [('M', x_colar, cy - r), ('L', x_cupula, cy - r), ('A', r, 0, 0, x_cupula, cy + r),
             ('L', x_colar, cy + r), ('Z',)]
    d.caminho(mover_cmds(corpo, 0.25, 0.3), '#000000', op=0.3)
    d.ret(x_colar + 0.25, cy - 2.9 + 0.3, 1.0, 5.8, '#000000', rx=0.25, op=0.3)
    if not transparente:
        d.caminho(corpo, '#18191c', stroke='#050506', sw=0.1)
        d.ret(x_ponta + 0.7, cy - 1.95, x_colar - x_ponta - 1.2, 0.6, '#ffffff', rx=0.3, op=0.16)
        d.circ(x_ponta + 1.1, cy - 1.15, 0.45, '#ffffff', op=0.22)
        d.ret(x_colar, cy - 2.9, 1.0, 5.8, '#121315', rx=0.25, stroke='#050506', sw=0.08)
        d.ret(x_colar + 0.15, cy - 2.75, 0.25, 5.5, '#ffffff', rx=0.1, op=0.08)
        return
    d.caminho(corpo, '#dfe6ea', stroke='#a9b4bb', sw=0.1, op=0.95)
    # a moldura de dentro: as duas pernas, a bandeira larga do catodo com o copo e a do anodo
    for s_ in (-1, 1):
        d.ret(x_colar - 3.6, cy + s_ * 1.27 - 0.25, 3.6, 0.5, '#aab3ba', op=0.9)
    d.poli([(x_colar - 3.4, cy + 0.35), (x_colar - 5.2, cy + 0.35), (x_colar - 5.2, cy + 1.75),
            (x_colar - 3.4, cy + 1.75)], '#9aa4ac', stroke='#7c868e', sw=0.06, op=0.9)
    d.ret_c(x_colar - 4.8, cy + 0.75, 0.9, 0.9, '#c7ced4', rx=0.15, op=0.9)            # o copo do chip
    d.ret_c(x_colar - 4.8, cy + 0.75, 0.3, 0.3, '#3a3f45')
    d.poli([(x_colar - 3.4, cy - 0.55), (x_colar - 4.4, cy - 0.55), (x_colar - 4.4, cy - 1.6),
            (x_colar - 3.4, cy - 1.6)], '#9aa4ac', stroke='#7c868e', sw=0.06, op=0.9)
    d.linha(x_colar - 4.3, cy - 0.65, x_colar - 4.75, cy + 0.6, '#5d666e', 0.05, curva=-0.25)   # o fio
    d.ret(x_ponta + 0.7, cy - 2.0, x_colar - x_ponta - 1.1, 0.75, '#ffffff', rx=0.35, op=0.55)
    d.circ(x_ponta + 1.05, cy - 1.1, 0.5, '#ffffff', op=0.6)
    d.caminho(corpo, 'url(#luz)', op=0.6)
    d.ret(x_colar, cy - 2.9, 1.0, 5.8, '#e8edf0', rx=0.25, stroke='#a9b4bb', sw=0.08, op=0.95)
    d.ret(x_colar + 0.15, cy - 2.75, 0.25, 5.5, '#ffffff', rx=0.1, op=0.5)


def lm393_de_pe(d, cx, cy, linhas, x_linhas):
    """O LM393 em SOP-8 de pe, as pernas a esquerda e a direita, a marcacao lida
    de baixo para cima (o pino 1 fica embaixo a direita, ao lado do ponto da
    serigrafia)."""
    for k in range(4):
        y = cy + (k - 1.5) * 1.27
        for s_ in (-1, 1):
            d.ret_c(cx + s_ * 3.2, y, 1.4, 0.64, ESTANHO, rx=0.06)
            d.ret(cx + s_ * 2.0 - (0 if s_ > 0 else 1.05), y - 0.21, 1.05, 0.42, 'url(#metal)', rx=0.05)
    E.sombra(d, cx - 2.0, cy - 2.5, 4.0, 5.0, rx=0.15)
    d.ret_c(cx, cy, 4.0, 5.0, PRETO_CI, rx=0.15)
    d.ret_c(cx, cy, 4.0, 5.0, 'url(#luz)', rx=0.15)
    d.linha(cx - 1.75, cy + 1.6, cx + 1.75, cy + 1.6, '#34363b', 0.1)
    d.circ(cx + 1.3, cy + 1.95, 0.3, '#2f3136')
    for (t, tam), x in zip(linhas, x_linhas):
        texto_girado(d, cx + x, cy, t, tam, MARCA_CI, -90, peso='normal')


def silk_colchetes(d, x0, y0, x1, y1, abertura, r=0.35, larg=0.16):
    """O contorno do capacitor na serigrafia: dois colchetes, abertos no meio de cima e de baixo."""
    xm = (x0 + x1) / 2
    for lado in (-1, 1):
        xa = x0 if lado < 0 else x1
        xi = xm + lado * abertura / 2
        d.caminho([('M', xi, y0), ('L', xa - lado * r, y0), ('A', r, 0, 0 if lado < 0 else 1, xa, y0 + r),
                   ('L', xa, y1 - r), ('A', r, 0, 0 if lado < 0 else 1, xa - lado * r, y1), ('L', xi, y1)],
                  None, stroke=SERIGRAFIA, sw=larg, cap='round')


def silk_moldura_aberta(d, x0, y0, x1, y1, x_abre, w_abre, embaixo, r=0.5, larg=0.16):
    """O contorno do LED, arredondado, com uma abertura no meio de um lado."""
    xa, xb = x_abre - w_abre / 2, x_abre + w_abre / 2
    if embaixo:
        cmds = [('M', xb, y1), ('L', x1 - r, y1), ('A', r, 0, 0, x1, y1 - r), ('L', x1, y0 + r), ('A', r, 0, 0, x1 - r, y0),
                ('L', x0 + r, y0), ('A', r, 0, 0, x0, y0 + r), ('L', x0, y1 - r), ('A', r, 0, 0, x0 + r, y1), ('L', xa, y1)]
    else:
        cmds = [('M', xb, y0), ('L', x1 - r, y0), ('A', r, 0, 1, x1, y0 + r), ('L', x1, y1 - r), ('A', r, 0, 1, x1 - r, y1),
                ('L', x0 + r, y1), ('A', r, 0, 1, x0, y1 - r), ('L', x0, y0 + r), ('A', r, 0, 1, x0 + r, y0), ('L', xa, y0)]
    d.caminho(cmds, None, stroke=SERIGRAFIA, sw=larg, cap='round')


def obstaculo_ir():
    """O modulo de obstaculo infravermelho (o FC-51, na placa MH-B), medido na
    foto de cima publicada pela SunFounder, retificada em 40 px/mm pelo passo
    das pernas do LM393: a placa de 31,5 x 13,3 mm, o receptor preto e o emissor
    transparente de 5 mm deitados para fora da ponta, os passivos 0805 da
    coluna, o trimpot 3362, o LM393 em SOP-8, os LEDs verdes de energia e de
    saida, o furo de 3 mm e a barra de tres pinos a 90 graus (VCC, GND, OUT).
    A camera nao estava a pino: o que e alto (o trimpot, os LEDs de 5 mm, a
    barra) saiu deslocado na foto, e vai para o lugar da base. O cobre vem da
    foto (vetorizado: o plano, as trilhas e o isolamento em volta das ilhas)."""
    nome = 'ir-obstacle-sensor'
    BW, BH = 31.5, 13.3
    X0 = 9.6
    L, H = caixa(X0 + BW + 6.2, 13.7)
    Y0 = (H - BH) / 2

    def P(x, y):                                                    # da foto retificada (mm) para a peca
        return X0 + x - 0.55, Y0 + y

    mascara, borda, cor_cobre = '#163d84', '#0b2552', '#2a5fbf'
    d = Desenho(T(A, 0, 0, A, 0, 0))
    E.sombra(d, X0, Y0, BW, BH, rx=0.4, op=0.35)
    d.ret(X0, Y0, BW, BH, mascara, rx=0.4, stroke=borda, sw=0.15)
    cobre_da_foto(d, 'mh-b', X0, Y0, cor_cobre)
    via(d, *P(8.18, 5.25), cor_cobre, '#0c1a3a', r=0.3)
    d.ret(X0, Y0, BW, BH, 'url(#luz)', rx=0.4)

    # a serigrafia
    for (x0, y0, x1, y1, ym) in ((0.85, 0.52, 3.75, 5.85, 3.08), (0.7, 7.05, 3.75, 12.55, 9.83)):
        E.silk_ret(d, *P(x0, y0), x1 - x0, y1 - y0, larg=0.16, rx=0.05)
        E.silk_linha(d, *P(x0, ym), *P(x1, ym), larg=0.16)
    for y0, y1 in ((0.47, 2.19), (3.1, 4.85)):
        silk_colchetes(d, *P(7.23, y0), *P(10.52, y1), abertura=0.32)
    for y0, y1 in ((5.7, 7.3), (8.33, 9.9), (11.04, 12.7)):
        E.silk_ret(d, *P(7.3, y0), 3.16, y1 - y0, larg=0.16, rx=0.12)
    for y0, y1 in ((1.85, 4.83), (8.1, 11.2)):
        E.silk_ret(d, *P(20.12, y0), 1.78, y1 - y0, larg=0.16, rx=0.12)
    silk_moldura_aberta(d, *P(22.08, 1.56), *P(27.4, 3.9), P(25.0, 0)[0], 0.45, embaixo=True)
    silk_moldura_aberta(d, *P(22.4, 9.2), *P(27.6, 11.6), P(25.3, 0)[0], 0.45, embaixo=False)
    for x, y in ((27.6, 1.48), (28.3, 11.65)):
        E.silk_linha(d, *P(x - 0.5, y), *P(x + 0.5, y), larg=0.16)
        E.silk_linha(d, *P(x, y - 0.6), *P(x, y + 0.6), larg=0.16)
    d.poli([P(32.05, 2.73), P(28.95, 2.73), P(28.95, 10.8), P(32.05, 10.8)], None, stroke=SERIGRAFIA, sw=0.16, fechado=False)
    for t, y in (('VCC', 3.7), ('GND', 6.6), ('OUT', 10.1)):
        texto_girado(d, *P(28.1, y), t, 0.95, SERIGRAFIA, -90)
    d.texto(*P(29.95, 0.83), 'MH-B', 0.85, SERIGRAFIA, alinhar='center', peso='bold')
    d.ret(*P(14.5, 12.62), 2.6, 0.4, None, rx=0.12, stroke=SERIGRAFIA, sw=0.14)       # a marca do lado do chip
    d.circ(*P(18.56, 13.0), 0.2, SERIGRAFIA)                                           # o ponto do pino 1

    # o furo de 3 mm (sem cobre em volta) e as ilhas das pernas dos LEDs de 5 mm
    d.circ(*P(25.05, 6.75), 1.5, '#0c0d0f')
    d.circ(*P(25.05, 6.75), 1.5, None, '#0a1f45', 0.15)
    for y in (2.08, 4.62, 8.53, 11.07):
        furo_metalizado(d, *P(2.35, y), 0.85, 0.42)

    # as pecas
    for y, tipo in ((1.3, 'C'), (3.96, 'C')):
        E.passivo(d, *P(9.1, y), '0805', tipo)
    for y, cod, giro in ((6.37, '103', 0), (9.03, '103', 180), (11.88, '101', 180)):
        E.passivo(d, *P(9.05, y), '0805', 'R')
        texto_girado(d, *P(9.05, y), cod, 0.55, '#e8e8e8', giro, peso='normal')
    for y in (3.17, 9.56):
        E.passivo(d, *P(21.06, y), '0805', 'R', vertical=True)
        texto_girado(d, *P(21.06, y), '102', 0.55, '#e8e8e8', -90, peso='normal')
    for x, y in ((25.08, 2.68), (25.29, 10.3)):
        for s_ in (-1, 1):
            d.ret_c(P(x, y)[0] + s_ * 1.42, P(x, y)[1], 1.05, 1.3, ESTANHO, rx=0.08)
        E.led_smd(d, *P(x, y), '#7fd99a', '0805')
    lm393_de_pe(d, *P(15.9, 10.17), [('LM393', 0.7), ('8AK', 0.7), ('908H', 0.7)], (-1.0, 0.0, 0.96))
    trimpot_3362(d, *P(15.75, 3.68), w=7.2, h=6.65)
    for t, x in (('3', 12.75), ('1', 18.8)):
        texto_girado(d, *P(x, 0.85), t, 0.62, '#173d82', 180)

    # a barra de pinos (VCC, GND, OUT) e os dois LEDs de 5 mm
    ys = [Y0 + 6.75 + (k - 1) * PASSO for k in range(3)]
    x_borda = X0 + BW
    barra_90(d, x_borda, ys, L - 0.15 - x_borda)
    for y_eixo, ponta, base, transparente in ((3.35, -8.6, 0.5, False), (9.8, -9.0, 0.3, True)):
        pernas(d, P(2.35, 0)[0], (Y0 + y_eixo - 1.27, Y0 + y_eixo + 1.27), P(base, 0)[0])
        led5_deitado(d, P(base, 0)[0], Y0 + y_eixo, P(ponta, 0)[0], transparente)

    gravar(nome, d, L, H)
    guardar(nome, L, H, [
        ('VCC', L - 0.9, ys[0], ['power'], 'Alimentacao: 3,3 a 5 V'),
        ('GND', L - 0.9, ys[1], ['power'], 'GND'),
        ('OUT', L - 0.9, ys[2], [], 'Saida digital: LOW com obstaculo na frente (o LED de saida acende); '
                                    'o trimpot ajusta o alcance, de 2 a 30 cm'),
    ])
    luz(nome, *P(25.08, 2.68), 0.55, 'verde', 'vcc-gnd')
    luz(nome, *P(25.29, 10.3), 0.55, 'verde', 'sensor-do')


obstaculo_ir()

# =====================================================================
#  tcrt5000: o modulo seguidor de linha (o sensor reflexivo TCRT5000)
# =====================================================================


def anel_furo(d, cx, cy, r_anel, r_furo, x_corte=None):
    """O furo de fixacao com o anel estanhado; x_corte: a borda da placa que corta o anel."""
    if x_corte is None or cx - r_anel >= x_corte:
        d.circ(cx, cy, r_anel, ESTANHO)
    else:
        dy = math.sqrt(max(0.0, r_anel ** 2 - (cx - x_corte) ** 2))
        d.caminho([('M', x_corte, cy - dy), ('A', r_anel, 1, 1, x_corte, cy + dy), ('Z',)], ESTANHO)
    d.circ(cx, cy, r_anel, 'url(#luz)', op=0.6)
    d.circ(cx, cy, r_furo + 0.12, None, '#7c838b', 0.1)
    d.circ(cx, cy, r_furo, '#0c0d0f')


def tcrt5000_sensor(d, x0, y0, x1, y1, emissor, receptor):
    """O TCRT5000 visto de cima: a caixa preta, a janela do emissor (o LED azul)
    e a do receptor (o fototransistor preto), com a parede no meio."""
    E.sombra(d, x0, y0, x1 - x0, y1 - y0, rx=0.3, op=0.4)
    d.ret(x0, y0, x1 - x0, y1 - y0, '#1c1d20', rx=0.3, stroke='#060607', sw=0.12)
    d.ret(x0 + 0.35, y0 + 0.35, x1 - x0 - 0.7, y1 - y0 - 0.7, None, rx=0.25, stroke='#2d2f33', sw=0.12)
    d.linha(x1 - 0.7, y0 + 0.75, x1 - 0.7, y1 - 0.75, '#33363b', 0.25)              # o degrau do lado
    (ex, ey, er), (rx_, ry, rr) = emissor, receptor
    c = 0.7
    janela = [(ex - 1.75 + c, ey - 1.65), (ex + 1.75 - c, ey - 1.65), (ex + 1.75, ey - 1.65 + c), (ex + 1.75, ey + 1.65 - c),
              (ex + 1.75 - c, ey + 1.65), (ex - 1.75 + c, ey + 1.65), (ex - 1.75, ey + 1.65 - c), (ex - 1.75, ey - 1.65 + c)]
    d.poli(janela, '#0f1012', stroke='#2a2c30', sw=0.1)
    d.circ(rx_, ry, rr + 0.32, '#0f1012', stroke='#2a2c30', sw=0.1)
    d.linha(x0 + 0.5, (ey + ry) / 2, x1 - 0.9, (ey + ry) / 2, '#2a2c30', 0.18)       # a parede entre os dois
    d.circ(ex + 0.1, ey + 0.12, er, '#000000', op=0.35)
    d.circ(ex, ey, er, '#1b86df', stroke='#0f4f8a', sw=0.08)
    d.circ(ex, ey, er * 0.7, '#3aa2ef', op=0.6)
    d.circ(ex, ey, er, 'url(#cupula)', op=0.85)
    d.circ(ex - er * 0.35, ey - er * 0.35, er * 0.22, '#ffffff', op=0.75)
    d.circ(rx_ + 0.1, ry + 0.12, rr, '#000000', op=0.35)
    d.circ(rx_, ry, rr, '#0c0d0f', stroke='#26282c', sw=0.08)
    d.circ(rx_, ry, rr, 'url(#cupula)', op=0.7)
    d.circ(rx_ - rr * 0.35, ry - rr * 0.35, rr * 0.2, '#ffffff', op=0.55)


def trimpot_rotor_branco(d, cx, cy, w, h, rotor, r, marca):
    """O trimpot de 6 mm de cima (o azul de rotor branco): a moldura azul
    quadrada, o disco branco com a fenda e o contato que aparece por ela."""
    E.sombra(d, cx - w / 2, cy - h / 2, w, h, rx=0.25, op=0.35)
    d.ret_c(cx, cy, w, h, '#2f7fd6', rx=0.25, stroke='#1a5aa6', sw=0.1)
    d.ret_c(cx, cy, w, h, 'url(#luz)', rx=0.25)
    rx_, ry = rotor
    d.circ(rx_ + 0.12, ry + 0.15, r, '#000000', op=0.3)
    d.circ(rx_, ry, r, '#eceef0', stroke='#b6bcc2', sw=0.08)
    d.circ(rx_, ry, r, 'url(#cupula)', op=0.35)
    d.ret_c(rx_ + 0.22, ry - 0.05, 1.2, 4.1, '#2a2c30', rx=0.4)                       # a fenda
    for y in (ry - 1.35, ry + 1.15):
        d.ret_c(rx_ + 0.22, y, 0.8, 0.5, '#c4c9cf', rx=0.1)
    texto_girado(d, rx_ - 1.8, ry - 0.05, marca, 0.9, '#a3a8ae', -90, peso='normal')
    d.ret_c(rx_ + 2.37, ry + 0.18, 1.0, 2.7, '#aab0b7', rx=0.12, stroke='#3d4a5c', sw=0.1)  # o contato do lado


def seguidor_tcrt5000():
    """O modulo seguidor de linha do TCRT5000 (a placa preta de 38,9 x 9,1 mm,
    tres pinos: S, V+ e G), medido na foto de cima publicada pela SunFounder
    (retificada em 40 px/mm pelo passo do LM393 e da barra): os dois furos de
    3,8 mm com o anel estanhado, o TCRT5000 atravessado na placa (o LED azul e
    o fototransistor), o trimpot de rotor branco, o LM393, o LED de sinal e
    os resistores com a marcacao da foto. Na placa preta o cobre quase nao
    aparece na foto, e por isso nao ha trilha desenhada: so o que se ve."""
    nome = 'tcrt5000'
    BW, BH = 38.85, 9.07
    X0 = 0.2
    L, H = caixa(X0 + BW + 6.2, 10.6)
    Y0 = 0.3

    def P(x, y):
        return X0 + x, Y0 + y

    mascara, borda, _ = E.MASCARAS['preta']
    d = Desenho(T(A, 0, 0, A, 0, 0))
    E.sombra(d, X0, Y0, BW, BH, rx=0.4, op=0.35)
    d.ret(X0, Y0, BW, BH, mascara, rx=0.4, stroke=borda, sw=0.15)
    d.ret(X0, Y0, BW, BH, 'url(#luz)', rx=0.4)
    anel_furo(d, *P(2.55, 4.5), 2.85, 1.88, x_corte=X0)
    anel_furo(d, *P(13.6, 4.44), 2.85, 1.9)

    # a serigrafia
    for t, x, y, giro, tam in (('T', 5.02, 1.0, 90, 0.8), ('R', 4.1, 8.4, 180, 0.95), ('IR_01', 13.0, 0.83, 180, 1.15),
                               ('R2', 15.95, 1.25, 90, 0.85), ('R3', 16.05, 8.15, -90, 0.85), ('R1', 25.05, 3.4, -90, 0.85),
                               ('IC1', 25.95, 8.1, -90, 0.85), ('S', 35.05, 1.3, -90, 1.0), ('V+', 35.05, 4.72, -90, 1.0),
                               ('G', 35.05, 8.3, -90, 1.0)):
        texto_girado(d, *P(x, y), t, tam, SERIGRAFIA, giro)
    for x0, y0, x1, y1 in ((16.95, 0.25, 20.15, 1.75), (16.9, 7.1, 20.27, 8.93), (25.75, 0.42, 28.92, 2.5)):
        E.silk_ret(d, *P(x0, y0), x1 - x0, y1 - y0, larg=0.16, rx=0.08)
    E.silk_linha(d, *P(16.95, 1.65), *P(16.95, 7.05), larg=0.16)
    E.silk_linha(d, *P(20.3, 8.6), *P(22.9, 8.6), larg=0.16)
    d.poli([P(31.3, 2.76), P(29.33, 2.76), P(29.33, 0.4), P(34.05, 0.4), P(34.05, 2.76), P(32.1, 2.76)],
           None, stroke=SERIGRAFIA, sw=0.16, fechado=False)
    E.silk_linha(d, *P(28.4, 3.75), *P(30.1, 3.75), larg=0.16)
    E.silk_linha(d, *P(30.6, 3.75), *P(32.1, 3.75), larg=0.16)
    d.poli([P(36.45, 0.75), P(36.45, 8.3), P(38.8, 8.3)], None, stroke=SERIGRAFIA, sw=0.16, fechado=False)

    # as pecas
    for x, y, cod, giro in ((18.5, 0.93, '391', 0), (18.54, 8.0, '103', 0), (27.6, 1.5, '202', 180)):
        E.passivo(d, *P(x, y), '0805', 'R')
        texto_girado(d, *P(x, y), cod, 0.55, '#e8e8e8', giro, peso='normal')
    lx, ly = P(31.75, 1.6)
    for s_ in (-1, 1):
        d.ret_c(lx + s_ * 1.6, ly, 1.15, 1.55, ESTANHO, rx=0.08)
    E.sombra(d, lx - 1.1, ly - 0.72, 2.2, 1.45)
    d.ret_c(lx, ly, 2.2, 1.45, '#f2efe6', rx=0.1)
    d.ret_c(lx, ly, 0.6, 0.6, '#e8d9c6', rx=0.08)
    d.ret_c(lx, ly, 0.25, 0.25, '#b79e76')
    d.ret_c(lx + 0.75, ly, 0.2, 1.3, '#2fa37a', op=0.85)
    d.ret_c(lx, ly, 2.2, 1.45, 'url(#luz)', rx=0.1)
    cx, cy = P(30.3, 6.13)
    for k in range(4):
        y = cy + (k - 1.5) * 1.27
        for s_ in (-1, 1):
            d.ret_c(cx + s_ * 2.95, y, 1.3, 0.6, ESTANHO, rx=0.06)
            d.ret(cx + s_ * 1.95 - (0 if s_ > 0 else 1.0), y - 0.2, 1.0, 0.4, 'url(#metal)', rx=0.05)
    E.sombra(d, cx - 1.95, cy - 2.5, 3.9, 5.0, rx=0.15)
    d.ret_c(cx, cy, 3.9, 5.0, PRETO_CI, rx=0.15)
    d.ret_c(cx, cy, 3.9, 5.0, 'url(#luz)', rx=0.15)
    d.circ(cx - 1.3, cy - 1.95, 0.3, '#2f3136')
    trimpot_rotor_branco(d, *P(21.07, 4.21), 6.82, 6.58, P(20.73, 4.27), 2.8, '103')
    tcrt5000_sensor(d, *P(5.42, -0.25), *P(10.65, 9.95), (*P(7.85, 2.8), 1.48), (*P(8.0, 6.9), 1.45))

    ys = [Y0 + 4.3 + (k - 1) * PASSO for k in range(3)]
    x_borda = X0 + BW
    barra_90(d, x_borda, ys, L - 0.15 - x_borda)
    gravar(nome, d, L, H)
    guardar(nome, L, H, [
        ('S', L - 0.9, ys[0], [], 'Saida digital: LOW sobre superficie clara (a luz infravermelha volta e o LED acende); '
                                 'HIGH sobre a linha preta ou sem nada perto (alcance de 1 a 8 mm)'),
        ('V+', L - 0.9, ys[1], ['power'], 'Alimentacao: 3,3 a 5 V'),
        ('GND', L - 0.9, ys[2], ['power'], 'GND (o "G" da placa)'),
    ])
    luz(nome, lx, ly, 0.55, 'vermelho', 'sensor-do')


seguidor_tcrt5000()

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
    """Os KY de 18,5 x 15 mm com a barra de tres pinos na direita."""
    L, H = caixa(24.6, 15.35)
    X0, Y0 = 0.2, (H - 15.0) / 2
    cy = H / 2
    ys = [cy - PASSO, cy, cy + PASSO]
    d = Desenho(T(A, 0, 0, A, 0, 0))
    desenho(d, X0, Y0, ys, L)
    gravar(nome, d, L, H)
    guardar(nome, L, H, [(n, L - 0.9, y, s, dsc) for (n, s, dsc), y in zip(descricoes, ys)])


def ntc(d, x0, y0, ys, L):
    """O KY-013 pela foto de cima: a placa universal e o NTC de gota preta com as
    pernas compridas, uma em cada ilha pequena do meio."""
    KEYES.placa(d, x0, y0, ys, L - 0.15 - (x0 + 18.5))
    texto_girado(d, x0 + 1.9, y0 + 7.8, 'A', 1.1, SERIGRAFIA, -90)
    texto_girado(d, x0 + 4.0, y0 + 7.8, 'S', 1.1, SERIGRAFIA, -90)
    gx, gy = x0 + 6.5, y0 + 10.0
    for px, py in ((x0 + 8.8, y0 + 9.4), (x0 + 8.6, y0 + 5.6)):
        KEYES.perna(d, gx + 0.3, gy - 0.3, px, py)
    d.circ(gx + 0.15, gy + 0.2, 1.0, '#000000', op=0.35)
    d.circ(gx, gy, 1.0, '#141518', stroke='#050506', sw=0.1)                 # a gota de epoxi
    d.circ(gx - 0.35, gy - 0.35, 0.32, '#ffffff', op=0.35)


def batimento(d, x0, y0, ys, L):
    """O KY-039 pela foto de cima (a placa em pe com a barra embaixo, deitada
    aqui: P(u, v) = (x0 + v, y0 + 15 - u)): o LED infravermelho de 5 mm em pe
    (o "Hr"), o fototransistor deitado na borda (o "Lcup"), o R1 (331) e o R2
    (103) na caixa dupla, as ilhas e o furo de fixacao."""
    def P(u, v):
        return x0 + v, y0 + 15.0 - u
    E.sombra(d, x0, y0, 18.5, 15.0, rx=0.4, op=0.35)
    d.ret(x0, y0, 18.5, 15.0, KEYES.PRETA, rx=0.4, stroke=KEYES.PRETA_BORDA, sw=0.15)
    KEYES.textura(d, x0 + 0.2, y0 + 0.2, 18.1, 14.6)
    d.ret(x0, y0, 18.5, 15.0, 'url(#luz)', rx=0.4)
    S = SERIGRAFIA
    d.linha(*P(2.8, 4.4), *P(9.2, 4.4), S, 0.2, cap='butt')
    d.linha(*P(3.2, 9.9), *P(12.3, 9.9), S, 0.2, cap='butt')
    for t, u, v in (('R', 4.4, 3.7), ('Hr', 8.2, 3.7)):
        texto_girado(d, *P(u, v), t, 1.2, S, -90)
    for t, u, v in (('Lcup', 1.8, 12.6), ('R1_2', 5.4, 12.6), ('R2_1', 12.3, 12.6)):
        texto_girado(d, *P(u, v), t, 0.95, S, 180)
    (xa, ya), (xb, yb) = P(6.2, 10.3), P(11.4, 14.5)
    d.ret(min(xa, xb), min(ya, yb), abs(xb - xa), abs(yb - ya), None, stroke=S, sw=0.18)
    xm, ym = P(8.8, 10.3)
    d.linha(xm, ym, xm, ym - 0.0 + (P(8.8, 14.5)[1] - ym), S, 0.18, cap='butt')
    (xa, ya), (xb, yb) = P(3.4, 15.0), P(11.6, 18.5)                      # o contorno da barra
    d.ret(min(xa, xb), min(ya, yb), abs(xb - xa), abs(yb - ya), None, stroke=S, sw=0.18)
    KEYES.ilha(d, *P(2.6, 2.0), 1.15, 0.95)                              # o furo de fixacao
    for u, v, r_a, r_f in ((5.6, 6.2, 0.95, 0.55), (8.5, 6.2, 0.85, 0.5), (11.1, 6.2, 0.9, 0.5),
                           (5.6, 8.7, 0.95, 0.55), (11.1, 8.7, 0.9, 0.5)):
        KEYES.ilha(d, *P(u, v), r_a, r_f)
    # os resistores (0805, em pe na foto: deitados aqui)
    E.passivo(d, *P(7.4, 12.1), '0805', 'R', '103')
    E.passivo(d, *P(10.2, 12.1), '0805', 'R', '331')
    # o fototransistor deitado na borda, as pernas nas ilhas da esquerda
    for u, v in ((2.8, 6.2), (2.8, 8.7)):
        KEYES.perna(d, *P(1.0, v), *P(u, v))
    (xa, ya), (xb, yb) = P(-0.3, 4.6), P(1.4, 9.6)
    d.ret(min(xa, xb), min(ya, yb), abs(xb - xa), abs(yb - ya), '#dfe7ec', rx=0.5, stroke='#9aa6ae', sw=0.1)
    d.ret(min(xa, xb), min(ya, yb), abs(xb - xa), abs(yb - ya), 'url(#cupula)', rx=0.5, op=0.6)
    # o LED infravermelho de 5 mm em pe (a lente transparente vista de cima) e a perna da direita
    KEYES.perna(d, *P(12.0, 7.4), *P(13.4, 8.7))
    lx, ly = P(12.6, 4.6)
    d.circ(lx + 0.25, ly + 0.3, 2.95, '#000000', op=0.35)
    d.circ(lx, ly, 2.95, '#e6eef2', stroke='#a9b6be', sw=0.12)            # a aba
    d.circ(lx, ly, 2.5, '#f3f7f9', stroke='#c3cdd3', sw=0.1)
    d.ret_c(lx - 0.4, ly, 0.9, 0.7, '#a3a9ad', rx=0.1)                     # o refletor
    d.circ(lx, ly, 2.5, 'url(#cupula)')
    d.circ(lx - 0.8, ly - 0.9, 0.5, '#ffffff', op=0.8)
    # a barra de tres pinos (deitada na borda da direita)
    xb = x0 + 18.5
    E.sombra(d, xb - 2.5, min(ys) - 1.27, 2.5, max(ys) - min(ys) + 2.54, rx=0.15)
    d.ret(xb - 2.5, min(ys) - 1.27, 2.5, max(ys) - min(ys) + 2.54, 'url(#plastico)', rx=0.15, stroke='#0b0c0d', sw=0.1)
    comp = L - 0.15 - xb
    for y in ys:
        d.ret(xb - 1.0, y - 0.32, comp + 1.0, 0.64, 'url(#metal)', stroke='#80868d', sw=0.05)
        d.ret(xb + comp - 0.5, y - 0.32, 0.5, 0.64, '#e9ecef')


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
    """O KY-037/KY-038 (35 x 15 mm), medido na foto de cima do KY-038: o
    recorte na ponta onde fica o microfone, as duas ilhas S e G dos bracos, o
    3296W (VR1), o LM393 em SOP-8, os 0603 (R1 103, R2 104, R5 102, R6 104),
    os LEDs L1 (energia) e L2 (o DO), o furo de 3 mm e a barra AO/G/+/DO. A
    foto tem a placa em pe com a barra embaixo: P(u, v) a deita com a barra a
    esquerda. O cobre nao aparece na foto (a mascara vermelha e opaca): so as
    vias que se veem."""
    X0 = 6.2
    # o microfone grande do KY-037 passa da ponta da placa (fica em pe nas pernas)
    sobra = 4.1 if r_mic > 4 else 0.0
    L, H = caixa(X0 + 35.0 + 0.2 + sobra, 15.4)
    Y0 = (H - 15.0) / 2
    cy = H / 2
    nomes = ['AOUT', 'GND', 'VCC', 'DOUT']
    ys = [cy + (k - 1.5) * PASSO for k in range(4)]

    def P(u, v):
        return X0 + 35.0 - v, Y0 + u
    mascara, borda = '#d81b1f', '#7a0b0e'
    d = Desenho(T(A, 0, 0, A, 0, 0))
    # a placa: o recorte de 8,7 x 5 mm na ponta do microfone
    contorno = [P(0, 0), P(3.3, 0), P(3.3, 5.0), P(12.0, 5.0), P(12.0, 0), P(15, 0), P(15, 35), P(0, 35)]
    d.poli([(x + 0.15, y + 0.2) for x, y in contorno], '#000000', op=0.35)
    d.poli(contorno, mascara, stroke=borda, sw=0.15)
    d.partes[-1] = d.partes[-1].replace('<path ', '<path stroke-linejoin="round" ', 1)
    d.poli(contorno, 'url(#luz)')
    for u, v in ((8.3, 11.0), (9.6, 12.2), (14.4, 12.2), (6.6, 20.4), (13.6, 33.3)):
        via(d, *P(u, v), '#e8333a', '#5a0a0c', r=0.36)
    # a serigrafia
    S = SERIGRAFIA

    def caixa_p(u0, v0, u1, v1):
        (xa, ya), (xb, yb) = P(u0, v0), P(u1, v1)
        d.ret(min(xa, xb), min(ya, yb), abs(xb - xa), abs(yb - ya), None, rx=0.15, stroke=S, sw=0.15)
    for caixa_ in ((0.8, 27.5, 2.6, 30.0), (13.2, 27.5, 15.0, 30.3), (0.8, 22.6, 2.6, 25.3), (13.2, 22.6, 15.0, 25.3),
                   (10.7, 8.4, 12.6, 11.3), (13.2, 8.4, 15.0, 11.3)):
        caixa_p(*caixa_)
    caixa_p(0.9, 6.5, 6.2, 22.4)                                        # o VR1
    (xa, ya), (xb, yb) = P(8.3, 14.3), P(14.8, 18.3)                    # o LM393, com o chanfro
    d.ret(min(xa, xb), min(ya, yb), abs(xb - xa), abs(yb - ya), None, stroke=S, sw=0.15)
    d.caminho([('M', *P(14.8, 15.8)), ('A', 0.55, 0, 0, *P(14.8, 16.9))], None, S, 0.15)
    for t, u, v, g in (('S', 2.5, 4.0, 90), ('+', 1.4, 5.5, 90), ('G', 13.4, 4.4, 90), ('+', 12.5, 6.4, 90),
                       ('R1', 14.0, 7.3, 90), ('L2', 4.2, 25.6, 90), ('VR1', 5.3, 23.0, 90)):
        texto_girado(d, *P(u, v), t, 0.9, S, g)
    for t, u, v in (('R2', 10.0, 9.8), ('R3+', 7.4, 10.6), ('L1', 13.6, 26.3), ('R6', 1.0, 31.6), ('R5', 14.0, 31.6)):
        texto_girado(d, *P(u, v), t, 0.9, S, 0)
    for t, y in (('AO', ys[0]), ('DO', ys[3])):                         # o G e o + ficam sob a barra
        d.texto(X0 + 4.6, y, t, 0.9, S, alinhar='center-left', peso='bold')
    d.poli([(X0 + 6.7, ys[3] + 0.5), (X0 + 7.3, ys[3] + 0.5), (X0 + 7.0, ys[3] - 0.1)], S)
    # os furos: as ilhas S e G dos bracos e o de fixacao de 3 mm
    for u in (2.0, 13.4):
        x, y = P(u, 2.3)
        d.circ(x, y, 0.95, ESTANHO)
        d.circ(x, y, 0.95, 'url(#luz)')
        d.circ(x, y, 0.5, '#0c0d0f')
    x, y = P(7.4, 25.6)
    d.circ(x, y, 1.75, '#0c0d0f')
    d.circ(x, y, 1.9, None, '#8e0f12', 0.25)
    # as pecas
    for u, v, cod in ((11.6, 9.8, '104'), (14.0, 9.8, '103'), (1.6, 28.7, '104'), (14.0, 29.0, '102')):
        E.passivo(d, *P(u, v), '0603', 'R', cod)
    for u in (1.6, 14.0):
        E.led_smd(d, *P(u, 24.0), '#ff3b30')
    # o LM393 (SOP-8): as pernas para a esquerda e a direita, o pino 1 no chanfro
    cxi, cyi = P(12.0, 16.2)
    for k in range(4):
        y = Y0 + 10.2 + k * 1.27
        for s_ in (-1, 1):
            d.ret_c(cxi + s_ * 3.0, y, 1.2, 0.62, ESTANHO, rx=0.06)
            d.ret_c(cxi + s_ * 2.35, y, 0.9, 0.42, 'url(#metal)', rx=0.05)
    E.sombra(d, cxi - 1.95, cyi - 2.45, 3.9, 4.9, rx=0.2)
    d.ret_c(cxi, cyi, 3.9, 4.9, PRETO_CI, rx=0.2)
    d.ret_c(cxi, cyi, 3.9, 4.9, 'url(#luz)', rx=0.2)
    d.circ(cxi - 1.3, cyi + 1.8, 0.28, '#3a3c41')
    texto_girado(d, cxi - 0.5, cyi, 'LM393', 0.85, MARCA_CI, -90, peso='normal')
    texto_girado(d, cxi + 0.6, cyi, '98M', 0.7, MARCA_CI, -90, peso='normal')
    # o 3296W (de cima: a face de 9,5 x 4,8 mm, o parafuso perto do LM393)
    trimpot_3296(d, *P(3.3, 11.75), parafuso=-1)
    # o microfone de eletreto, em pe sobre o recorte: as duas pernas e a lata com o feltro
    for u, v in ((8.0, 9.0), (12.3, 7.0)):
        x, y = P(u, v)
        d.ret_c(x, y, 1.2, 1.6, ESTANHO, rx=0.6)
    cx = X0 + 35.0 - 1.0 if r_mic > 4 else P(8.0, 4.3)[0]
    cym = Y0 + 8.0
    d.circ(cx + 0.3, cym + 0.35, r_mic, '#000000', op=0.35)
    d.circ(cx, cym, r_mic, 'url(#lata)', stroke='#6c737b', sw=0.15)
    d.circ(cx, cym, r_mic - 0.55, '#1c1d20')
    for k in range(int(r_mic * 2.2)):
        a = 2 * math.pi * k / int(r_mic * 2.2)
        d.circ(cx + (r_mic - 1.3) * 0.5 * math.cos(a), cym + (r_mic - 1.3) * 0.5 * math.sin(a), 0.18, '#2b2d31')
    d.circ(cx, cym, r_mic - 0.55, 'url(#cupula)', op=0.3)
    # a barra a 90 graus, deitada sobre a placa
    barra_90_esq(d, X0 + 1.2, ys, X0 + 1.2 - 0.15)
    gravar(nome, d, L, H)
    desc = {'AOUT': (['analog'], 'Saida analogica: o som do microfone'), 'GND': (['power'], 'GND (G na placa)'),
            'VCC': (['power'], 'Alimentacao (+ na placa): 3,3 V ou 5 V'),
            'DOUT': ([], 'Saida digital: HIGH com o som acima do ponto do trimpot (o L2 acende)')}
    guardar(nome, L, H, [(n, 0.9, y, *desc[n]) for n, y in zip(nomes, ys)])
    luz(nome, *P(14.0, 24.0), 0.55, 'vermelho', 'vcc-gnd')
    luz(nome, *P(1.6, 24.0), 0.55, 'vermelho', 'sensor-do')


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
Y_GY = Y0 + 14.4
# Pela foto de cima do GY-521 (o lado dos componentes): com a barra embaixo, o VCC
# fica na esquerda -- a foto tem a barra em cima e e girada 180 graus. A foto
# mede 18,35 mm entre o INT e o VCC; os 17,78 de verdade dao a escala.
ESC_GY = 17.78 / 18.35


def G(u, v):
    return X0 + 10.5 - (u - 10.425) * ESC_GY, Y0 + 8.0 - (v - 8.0) * ESC_GY


d = Desenho(T(A, 0, 0, A, 0, 0))
E.sombra(d, X0, Y0, 21.0, 16.0, rx=0.5, op=0.35)
d.ret(X0, Y0, 21.0, 16.0, '#1e56b8', rx=0.5, stroke='#0f2f6e', sw=0.15)
cor_cobre = '#2f6ccf'
for pts in (((10.2, 2.0), (10.2, 4.0)), ((11.7, 2.0), (12.9, 3.2), (12.9, 6.0)), ((14.3, 2.0), (15.6, 4.6)),
            ((17.0, 2.0), (17.0, 4.8), (16.0, 5.6)), ((19.6, 2.0), (19.6, 5.4)), ((8.0, 3.2), (8.0, 6.6)),
            ((6.4, 2.0), (7.4, 3.6), (7.4, 6.6)), ((5.5, 12.4), (8.6, 12.4)), ((12.0, 11.6), (13.2, 12.6))):
    trilha(d, [G(u, v) for u, v in pts], cor_cobre, 0.36)
for u, v in ((4.9, 4.0), (8.4, 4.5), (15.8, 5.1), (12.7, 4.2), (16.1, 9.6), (13.9, 13.2), (8.8, 11.9)):
    via(d, *G(u, v), cor_cobre, '#0d2a63')
d.ret(X0, Y0, 21.0, 16.0, 'url(#luz)', rx=0.5)
S = SERIGRAFIA
d.linha(*G(0.5, 2.6), *G(20.5, 2.6), S, 0.2, cap='butt')
for k, (x, n) in enumerate(zip(XS_GY, NOMES_GY)):
    texto_girado(d, x, Y0 + 11.7, n, 0.82, S, -90)
for u0, v0, u1, v1 in ((3.2, 5.2, 7.2, 7.9), (4.0, 9.8, 7.2, 12.4), (12.8, 5.6, 17.6, 8.6), (12.8, 9.6, 16.1, 12.0),
                       (17.6, 5.4, 20.9, 9.4), (18.2, 10.3, 20.8, 12.0), (7.6, 12.6, 13.2, 15.0)):
    (xa, ya), (xb, yb) = G(u0, v0), G(u1, v1)
    d.ret(min(xa, xb), min(ya, yb), abs(xb - xa), abs(yb - ya), None, rx=0.15, stroke=S, sw=0.15)
texto_girado(d, *G(10.4, 15.4), 'ITG/MPU6050', 1.05, S, 180)
for t, u, v, g in (('C12', 6.0, 8.6, 90), ('C13', 6.2, 13.0, 90), ('C102', 13.2, 15.0, 180), ('C10', 15.4, 12.6, 90),
                   ('C11', 14.0, 12.6, 90), ('C3', 16.6, 9.4, 90)):
    texto_girado(d, *G(u, v), t, 0.62, S, g)
ax, ay = G(2.2, 10.4)                                                           # os eixos X e Y
d.linha(ax, ay, ax, ay + 2.2, S, 0.18)
d.linha(ax, ay, ax + 2.2, ay, S, 0.18)
d.poli([(ax - 0.3, ay + 1.9), (ax + 0.3, ay + 1.9), (ax, ay + 2.4)], S)
d.poli([(ax + 1.9, ay - 0.3), (ax + 1.9, ay + 0.3), (ax + 2.4, ay)], S)
texto_girado(d, ax - 0.6, ay + 2.6, 'Y', 0.7, S, 180)
texto_girado(d, ax + 2.6, ay - 0.6, 'X', 0.7, S, 180)
for x in XS_GY:                                                                  # as ilhas da barra
    d.circ(x, Y_GY, 0.95, '#c79b34')
    d.circ(x, Y_GY, 0.95, 'url(#luz)')
    d.circ(x, Y_GY, 0.5, '#0c0d0f')
d.ret_c(XS_GY[0], Y_GY, 1.9, 1.9, None, stroke='#c79b34', sw=0.25)
for u, v in ((2.9, 13.9), (18.4, 13.9)):                                         # os furos de fixacao, dourados
    x, y = G(u, v)
    d.circ(x, y, 1.6, '#c79b34')
    d.circ(x, y, 1.6, 'url(#luz)')
    d.circ(x, y, 1.3, '#0c0d0f')
# as pecas
E.led_smd(d, *G(4.0, 6.5), '#3aff6a', vertical=True)
E.passivo(d, *G(5.0, 6.5), '0603', 'R', '472', vertical=True)
E.passivo(d, *G(6.3, 6.5), '0603', 'C', vertical=True)
E.passivo(d, *G(5.0, 11.0), '0603', 'R', '102', vertical=True)
E.passivo(d, *G(6.3, 11.0), '0603', 'C', vertical=True)
E.passivo(d, *G(13.7, 7.2), '0603', 'R', '222', vertical=True)
E.passivo(d, *G(15.3, 7.2), '0603', 'R', '222', vertical=True)
E.passivo(d, *G(16.8, 7.2), '0603', 'C', vertical=True, cor='#8a5a3a')
E.passivo(d, *G(13.7, 11.0), '0603', 'C', vertical=True)
E.passivo(d, *G(15.3, 11.0), '0603', 'C', vertical=True)
E.passivo(d, *G(19.6, 11.2), '0805', 'C', cor='#8a5a3a')
# o regulador (SOT-23-5, 4A2D): tres pernas de um lado, duas do outro
rx_, ry_ = G(19.2, 7.2)
for k, dx in enumerate((-0.95, 0.0, 0.95)):
    d.ret_c(rx_ + dx, ry_ + 1.35, 0.42, 0.8, ESTANHO)
for dx in (-0.95, 0.95):
    d.ret_c(rx_ + dx, ry_ - 1.35, 0.42, 0.8, ESTANHO)
E.sombra(d, rx_ - 1.45, ry_ - 0.8, 2.9, 1.6)
d.ret_c(rx_, ry_, 2.9, 1.6, PRETO_CI, rx=0.1)
d.ret_c(rx_, ry_, 2.9, 1.6, 'url(#luz)', rx=0.1)
texto_girado(d, rx_, ry_, '4A2D', 0.55, MARCA_CI, 180, peso='normal')
# o tantalo de 10 uF (106C), a faixa do + do lado do C102
tx, ty = G(10.3, 14.0)
for s_ in (-1, 1):
    d.ret_c(tx + s_ * 1.9, ty, 1.2, 1.9, ESTANHO, rx=0.1)
E.sombra(d, tx - 1.75, ty - 1.4, 3.5, 2.8, rx=0.2)
d.ret_c(tx, ty, 3.5, 2.8, '#e0a43a', rx=0.25, stroke='#a8741e', sw=0.08)
d.ret_c(tx + 1.3, ty, 0.5, 2.8, '#b77d20')
d.ret_c(tx, ty, 3.5, 2.8, 'url(#luz)', rx=0.25)
texto_girado(d, tx - 0.2, ty - 0.45, '106C', 0.6, '#5a3410', 180, peso='bold')
texto_girado(d, tx - 0.2, ty + 0.45, '510N', 0.55, '#5a3410', 180, peso='normal')
# o MPU-6050 (QFN-24 de 4 x 4 mm)
qx, qy = G(10.0, 8.3)
for k in range(6):
    o = (k - 2.5) * 0.5
    for s_ in (-1, 1):
        d.ret_c(qx + o, qy + s_ * 2.2, 0.26, 0.5, ESTANHO)
        d.ret_c(qx + s_ * 2.2, qy + o, 0.5, 0.26, ESTANHO)
E.sombra(d, qx - 2.0, qy - 2.0, 4.0, 4.0, rx=0.12)
d.ret_c(qx, qy, 4.0, 4.0, '#2a2c31', rx=0.12)
d.ret_c(qx, qy, 4.0, 4.0, 'url(#luz)', rx=0.12)
d.circ(qx + 1.45, qy + 1.45, 0.22, '#3a3c41')
for k, t in enumerate(('MPU-6050', 'C3V18', 'EL1726')):
    texto_girado(d, qx, qy + (k - 1) * 0.75, t, 0.5, MARCA_CI, 180, peso='normal')
gravar('mpu6050', d, L, H)
DESC_GY = {'VCC': (['power'], 'Alimentacao: 3,3 a 5 V (o regulador da placa faz os 3,3 V)'), 'GND': (['power'], 'GND'),
           'SCL': ([], 'I2C: o relogio (SCL da placa)'), 'SDA': ([], 'I2C: os dados (SDA da placa)'),
           'XDA': ([], 'I2C auxiliar: dados de um sensor de fora (magnetometro)'), 'XCL': ([], 'I2C auxiliar: o relogio'),
           'AD0': ([], 'O endereco: LOW (ou solto) = 0x68, HIGH = 0x69'), 'INT': ([], 'Interrupcao: avisa que ha dado novo')}
guardar('mpu6050', L, H, [(n, x, Y_GY, *DESC_GY[n]) for n, x in zip(NOMES_GY, XS_GY)])
luz('mpu6050', *G(4.0, 6.5), 0.55, 'verde', 'vcc-gnd')

# =====================================================================
#  hx711: o modulo verde comum (XFW-HX711, 24,2 x 15,7 mm)
# =====================================================================
L, H = caixa(24.6, 16.1)
X0, Y0 = 0.2, 0.2
LP, AP = 24.2, 15.7
cy = Y0 + AP / 2
NOMES_HX = ['GND', 'DT', 'SCK', 'VCC']
X_HX = X0 + LP - 2.3                                              # os quatro furos da direita
YS_HX = [cy + (k - 1.5) * PASSO for k in range(4)]
X_CEL = X0 + 1.6                                                  # a coluna dos seis da celula, na esquerda
YS_CEL = [cy + (k - 2.5) * PASSO for k in range(6)]
d = Desenho(T(A, 0, 0, A, 0, 0))
placa(d, X0, Y0, LP, AP, '#1d8a3e', '#0f4a20', rx=0.5)
for k, (y, n) in enumerate(zip(YS_CEL, ('E+', 'E-', 'A-', 'A+', 'B-', 'B+'))):
    ilha(d, X_CEL, y, quadrada=k == 0)
    d.texto(X_CEL + 1.3, y, n, 0.85, BRANCO, alinhar='center-left', peso='normal')
d.ret(X_HX - 1.3, YS_HX[0] - 1.5, 2.6, YS_HX[-1] - YS_HX[0] + 3.0, None, rx=0.15, stroke=BRANCO, sw=0.12)  # a moldura da barra
for k, (y, n) in enumerate(zip(YS_HX, NOMES_HX)):
    ilha(d, X_HX, y, quadrada=k == 0)
    d.texto(X_HX - 1.6, y, n, 0.85, BRANCO, alinhar='center-right', peso='normal')
# o HX711 (SOP-16), em pe no meio da placa
CX_CI, CY_CI = X0 + 13.2, Y0 + 6.6
for k in range(8):
    for s_ in (-1, 1):
        d.ret_c(CX_CI + s_ * 2.55, CY_CI - 4.445 + k * 1.27, 1.2, 0.42, ESTANHO)
d.ret_c(CX_CI, CY_CI, 4.0, 10.0, '#202124', rx=0.15)
d.circ(CX_CI - 1.2, CY_CI - 4.2, 0.28, '#3a3c40')
d.texto(CX_CI, CY_CI, 'HX711', 0.75, '#c8cbd0', ang=90, alinhar='center')
# o transistor do E+ (S8550, SOT-23) e os passivos
d.ret_c(X0 + 7.2, Y0 + 2.4, 1.4, 2.9, '#202124')
for y in (Y0 + 1.45, Y0 + 3.35):
    d.ret_c(X0 + 6.2, y, 0.7, 0.4, ESTANHO)
d.ret_c(X0 + 8.2, Y0 + 2.4, 0.7, 0.4, ESTANHO)
for k in range(4):                                                # a coluna de resistores e capacitores
    smd(d, X0 + 7.6, Y0 + 7.0 + k * 1.9, 1.6, 0.8, '#a37a52' if k % 2 else '#1a1a1a')
for k in range(3):
    smd(d, X0 + 9.6, Y0 + 9.0 + k * 1.9, 1.6, 0.8, '#1a1a1a' if k % 2 else '#a37a52')
for x in (X0 + 12.6, X0 + 16.4):                                  # a fileira de cima
    smd(d, x, Y0 + 0.9, 1.6, 0.8, '#a37a52')
smd(d, X0 + 21.2, Y0 + 1.0, 1.6, 0.8, '#1a1a1a')
d.texto(X0 + 14.9, Y0 + AP - 1.4, 'XFW-HX711', 1.15, BRANCO, alinhar='center')
gravar('hx711', d, L, H)
DESC_HX = {'GND': (['power'], 'GND'), 'DT': ([], 'Os dados (DOUT do HX711)'), 'SCK': ([], 'O relogio (PD_SCK do HX711)'),
           'VCC': (['power'], 'Alimentacao: 2,6 a 5,5 V')}
guardar('hx711', L, H, [(n, X_HX, y, *DESC_HX[n]) for n, y in zip(NOMES_HX, YS_HX)])

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
