"""python ferramentas/extrair-svg/pecas-mostradores.py

As saidas novas da Alpha 25, no estilo das placas e no tamanho real (as
medidas dos fornecedores):

  - tm1637          : o modulo de 4 digitos com os dois pontos do relogio (a
                      placa azul de 42 x 24 mm, os quatro furos M2, o display
                      de 0,36" de 30 x 14 mm e a barra CLK, DIO, VCC, GND na
                      ponta). A cor do display e o modelo da peca.
  - max7219-matriz  : a matriz 8 x 8 do MAX7219 (o kit de 50 x 32 mm): a
                      matriz 1088AS de 32 x 32 mm (os LEDs de 3 mm no passo de
                      4 mm), a barra de entrada (VCC, GND, DIN, CS, CLK) e a de
                      saida (VCC, GND, DOUT, CS, CLK) para encadear outra.
  - modulo-semaforo : o semaforo de 56 x 13 mm, preto, com os LEDs de 8 mm
                      (vermelho, amarelo e verde, com os resistores de 330,
                      220 e 470 ohms) e a barra G, Y, R, GND embaixo.

O que acende -- os segmentos do TM1637, os pontos da matriz, os LEDs do
semaforo -- sai em px da caixa para app/DesenhoDosMostradores.cs; o editor
pinta por cima.
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
AZUL, AZUL_BORDA, BRANCO = '#1d4fa6', '#0f2c66', '#f3f3f1'

DEFS_EXTRA = '''  <radialGradient id="cupula" cx="0.38" cy="0.32" r="0.75"><stop offset="0" stop-color="#ffffff" stop-opacity="0.55"/><stop offset="0.35" stop-color="#ffffff" stop-opacity="0.08"/><stop offset="1" stop-color="#000000" stop-opacity="0.35"/></radialGradient>
'''

pecas = []
cs_segmentos, cs_pontos, cs_luzes = [], [], []


def svg_de(d, L, H):
    w, h = L * A, H * A
    return (f'<svg xmlns="http://www.w3.org/2000/svg" version="1.1" width="{f(w)}px" height="{f(h)}px" '
            f'viewBox="0 0 {f(w)} {f(h)}">\n<defs>\n{pa.DEFS}{DEFS_EXTRA}</defs>\n' + ''.join(d.partes) + '</svg>\n')


def gravar(nome, svg):
    io.open(pa.BASE + f'app/assets/{nome}.svg', 'w', encoding='utf-8', newline='').write(svg)


def guardar(id_peca, L, H, pinos):
    pecas.append({'id': id_peca, 'largura': round(L * A), 'altura': round(H * A),
                  'pinos': [{'nome': n, 'x': round(x * A, 2), 'y': round(y * A, 2), 'sinais': s, 'descricao': dsc}
                            for n, x, y, s, dsc in pinos],
                  'variantes': [], 'metades': False})
    print(f'{id_peca}: {round(L * A)}x{round(H * A)} px, {len(pinos)} pinos')


def furo(d, x, y, furo_d, anel_d, fundo='#0d1a2e'):
    d.circ(x, y, anel_d / 2, ESTANHO)
    d.circ(x, y, furo_d / 2, fundo)


def pino_soldado(d, x, y, quadrado=False):
    if quadrado:
        d.ret_c(x, y, 1.7, 1.7, ESTANHO)
    else:
        d.circ(x, y, 0.85, ESTANHO)
    d.circ(x, y, 0.55, '#dfe3e7')
    d.circ(x - 0.15, y - 0.15, 0.2, '#ffffff', op=0.7)


def segmentos_do_digito(cx, cy, w, h, t, inclinacao=8.0):
    """Os sete segmentos de um digito inclinado (os hexagonos de A a G), em mm."""
    k = math.tan(math.radians(inclinacao))
    sx = lambda x, y: (cx + x - (y - cy) * k, y)                   # inclina para a direita
    meio, topo, base = cy, cy - h / 2, cy + h / 2
    x0, x1 = cx - w / 2, cx + w / 2
    g = t * 0.12

    def horizontal(y):
        return [sx(x0 - cx + t * 0.5 + g, y), sx(x0 - cx + t + g, y - t / 2), sx(x1 - cx - t - g, y - t / 2),
                sx(x1 - cx - t * 0.5 - g, y), sx(x1 - cx - t - g, y + t / 2), sx(x0 - cx + t + g, y + t / 2)]

    def vertical(x, y0, y1):
        return [sx(x - cx, y0 + g), sx(x - cx + t / 2, y0 + t / 2 + g), sx(x - cx + t / 2, y1 - t / 2 - g),
                sx(x - cx, y1 - g), sx(x - cx - t / 2, y1 - t / 2 - g), sx(x - cx - t / 2, y0 + t / 2 + g)]

    xe, xd = x0 + t / 2, x1 - t / 2
    return {'A': horizontal(topo + t / 2), 'G': horizontal(meio), 'D': horizontal(base - t / 2),
            'F': vertical(xe, topo + t / 2, meio), 'B': vertical(xd, topo + t / 2, meio),
            'E': vertical(xe, meio, base - t / 2), 'C': vertical(xd, meio, base - t / 2)}


def circulo(cx, cy, r, n=14):
    return [(cx + r * math.cos(2 * math.pi * k / n), cy + r * math.sin(2 * math.pi * k / n)) for k in range(n)]


def em_px(pontos):
    return '[' + ', '.join(f'({x * A:.2f}, {y * A:.2f})' for x, y in pontos) + ']'


# =====================================================================
#  TM1637 de 4 digitos com os dois pontos
# =====================================================================
L, H = 159 / A, 91 / A                                       # 42,1 x 24,1 mm: a caixa inteira
PINOS_TM = [('CLK', [], 'O relogio do TM1637 (qualquer pino digital: o CLK do TM1637Display)'),
            ('DIO', [], 'Os dados do TM1637 (qualquer pino digital: o DIO do TM1637Display)'),
            ('VCC', ['power'], 'Alimentacao: 3,3 a 5 V'), ('GND', ['power'], 'GND')]
X_TM = 40.0
YS_TM = [H / 2 + (k - 1.5) * PASSO for k in range(4)]
DX0, DY0, DX1, DY1 = 4.0, 5.0, 34.0, 19.0                    # o display de 30 x 14 mm
CENTROS = [9.3, 15.9, 24.1, 30.7]
segs_tm = {}
d = Desenho(T(A, 0, 0, A, 0, 0))
d.ret(0, 0, L, H, AZUL, rx=0.8, stroke=AZUL_BORDA, sw=0.15)
d.ret(0, 0, L, H, 'url(#luz)', rx=0.8)
for x in (2.2, L - 2.2):
    for y in (2.2, H - 2.2):
        furo(d, x, y, 2.2, 3.2)
d.ret(DX0, DY0, DX1 - DX0, DY1 - DY0, '#1a1b1e', rx=0.3, stroke='#08090a', sw=0.12)  # o corpo do display
d.ret(DX0 + 0.5, DY0 + 0.5, DX1 - DX0 - 1.0, DY1 - DY0 - 1.0, '#0f1012', rx=0.2)
for k, cx in enumerate(CENTROS):
    for nome, poli in segmentos_do_digito(cx, (DY0 + DY1) / 2, 4.6, 9.14, 0.95).items():
        d.poli(poli, '#3a3a3c', '#2a2a2c', 0.04)                  # o segmento apagado
        segs_tm[f'D{k + 1}{nome}'] = poli
for y in (10.0, 14.0):                                            # os dois pontos
    d.circ(20.0, y, 0.55, '#3a3a3c')
segs_tm['COLON'] = circulo(20.0, 10.0, 0.55, 10) + circulo(20.0, 14.0, 0.55, 10)[::-1]
d.ret(DX0 + 0.5, DY0 + 0.5, DX1 - DX0 - 1.0, 2.0, '#ffffff', op=0.05)
for k, y in enumerate(YS_TM):
    pino_soldado(d, X_TM, y, quadrado=k == 0)
    d.texto(X_TM - 1.5, y, PINOS_TM[k][0], 0.7, BRANCO, alinhar='center-right')
gravar('tm1637', svg_de(d, L, H))
guardar('tm1637', L, H, [(n, X_TM, YS_TM[k], s, dsc) for k, (n, s, dsc) in enumerate(PINOS_TM)])
cs_segmentos.append(('tm1637', segs_tm))


# =====================================================================
#  matriz 8 x 8 do MAX7219
# =====================================================================
L, H = 50.0, 32.0
ENTRADA = [('VCC', ['power'], 'Alimentacao: 5 V (a matriz toda acesa puxa ~300 mA)'), ('GND', ['power'], 'GND'),
           ('DIN', [], 'Os dados (o DIN da LedControl)'), ('CS', [], 'O chip select (o CS da LedControl)'),
           ('CLK', [], 'O relogio (o CLK da LedControl)')]
SAIDA = [('VCC.2', ['power'], 'O VCC, para a proxima matriz'), ('GND.2', ['power'], 'O GND, para a proxima matriz'),
         ('DOUT', [], 'Os dados para o DIN da proxima matriz (a cadeia da LedControl)'),
         ('CS.2', [], 'O CS, para a proxima matriz'), ('CLK.2', [], 'O CLK, para a proxima matriz')]
YS_MX = [H / 2 + (k - 2) * PASSO for k in range(5)]
X_ENT, X_SAI = 2.4, L - 2.4
MX0, MY0 = 9.0, 0.0
centros = [(MX0 + 2.0 + 4.0 * c, MY0 + 2.0 + 4.0 * r) for r in range(8) for c in range(8)]
d = Desenho(T(A, 0, 0, A, 0, 0))
d.ret(0, 0, L, H, AZUL, rx=0.8, stroke=AZUL_BORDA, sw=0.15)
d.ret(0, 0, L, H, 'url(#luz)', rx=0.8)
for k, (n, _, _) in enumerate(ENTRADA):
    pino_soldado(d, X_ENT, YS_MX[k], quadrado=k == 0)
    d.texto(X_ENT + 1.5, YS_MX[k], n, 0.62, BRANCO, alinhar='center-left')
for k, (n, _, _) in enumerate(SAIDA):
    pino_soldado(d, X_SAI, YS_MX[k], quadrado=k == 0)
    d.texto(X_SAI - 1.5, YS_MX[k], n.split('.')[0], 0.62, BRANCO, alinhar='center-right')
d.ret(MX0, MY0, 32.0, 32.0, '#141416', rx=0.3, stroke='#050506', sw=0.15)            # a matriz 1088AS
for x, y in centros:
    d.circ(x, y, 1.5, '#2a1414', stroke='#3c2020', sw=0.12)
    d.circ(x - 0.35, y - 0.4, 0.45, '#ffffff', op=0.08)
d.ret(MX0, MY0 + 0.2, 32.0, 2.0, '#ffffff', op=0.04)
gravar('max7219-matriz', svg_de(d, L, H))
guardar('max7219-matriz', L, H, [(n, X_ENT, YS_MX[k], s, dsc) for k, (n, s, dsc) in enumerate(ENTRADA)]
        + [(n, X_SAI, YS_MX[k], s, dsc) for k, (n, s, dsc) in enumerate(SAIDA)])
cs_pontos.append(('max7219-matriz', centros, 1.5))


# =====================================================================
#  o semaforo
# =====================================================================
L, H = 49 / A, 231 / A                                       # 13 x 61 mm: a placa de 56 mm e a barra que sai embaixo
PINOS_SEM = [('G', [], 'O LED verde (com o resistor de 470 ohms dentro): HIGH acende'),
             ('Y', [], 'O LED amarelo (com o resistor de 220 ohms dentro): HIGH acende'),
             ('R', [], 'O LED vermelho (com o resistor de 330 ohms dentro): HIGH acende'),
             ('GND', ['power'], 'GND (o catodo comum dos tres)')]
XS_SEM = [L / 2 + (k - 1.5) * PASSO for k in range(4)]
Y_PONTA = H - 0.8
LEDS = [('R', 9.0, '#c8261e'), ('Y', 22.5, '#e5b81d'), ('G', 36.0, '#2fa046')]
d = Desenho(T(A, 0, 0, A, 0, 0))
for x in XS_SEM:                                             # os pinos da barra, para fora da placa
    d.ret(x - 0.32, 57.0, 0.64, Y_PONTA - 57.0 + 0.4, 'url(#ouro)')
d.ret(0, 0, L, 56.0, '#151619', rx=0.8, stroke='#050506', sw=0.15)
d.ret(0, 0, L, 56.0, 'url(#luz)', rx=0.8)
d.ret(XS_SEM[0] - 1.27, 54.5, 4 * PASSO, 2.5, 'url(#plastico)', rx=0.2)              # o plastico da barra
furo(d, L / 2, 47.0, 3.0, 4.2, fundo='#2a2b2f')
for k, x in enumerate(XS_SEM):
    d.texto(x, 52.4, PINOS_SEM[k][0], 0.75, BRANCO, alinhar='center')
for nome, y, cor in LEDS:
    d.circ(L / 2, y, 4.6, cor, stroke='#0b0b0c', sw=0.12, op=0.85)                     # o colar do LED de 8 mm
    d.circ(L / 2, y, 4.0, cor)
    d.circ(L / 2, y, 4.0, 'url(#cupula)')
    d.circ(L / 2, y, 1.2, '#000000', op=0.15)                                           # o chip, por dentro
gravar('modulo-semaforo', svg_de(d, L, H))
guardar('modulo-semaforo', L, H, [(n, XS_SEM[k], Y_PONTA, s, dsc) for k, (n, s, dsc) in enumerate(PINOS_SEM)])
cs_luzes.append(('modulo-semaforo', [(L / 2, y, 4.0, cor, nome) for nome, y, cor in LEDS]))


cam = pa.BASE + 'app/assets/pecas.json'
lista = json.load(io.open(cam, encoding='utf-8'))
novos = {p['id'] for p in pecas}
lista = [p for p in lista if p['id'] not in novos] + pecas
lista.sort(key=lambda p: p['id'])
io.open(cam, 'w', encoding='utf-8', newline='').write(json.dumps(lista, ensure_ascii=False, indent=1))
print(f'catalogo: {len(lista)} pecas')

linhas = ['namespace Simulador.App;', '', '// GERADO por ferramentas/extrair-svg/pecas-mostradores.py -- nao edite a mao.', '//',
          '// O que acende nas saidas novas (px da caixa): os segmentos do TM1637, os',
          '// pontos da matriz do MAX7219 e os LEDs do semaforo.',
          'static class DesenhoDosMostradores', '{',
          '    /// <summary>O contorno de cada segmento (D1A ... D4G e os dois pontos, o COLON).</summary>',
          '    public static readonly Dictionary<string, Dictionary<string, (double X, double Y)[]>> Segmentos = new()', '    {']
for tipo, segs in cs_segmentos:
    linhas.append(f'        ["{tipo}"] = new()')
    linhas.append('        {')
    for nome, poli in segs.items():
        linhas.append(f'            ["{nome}"] = {em_px(poli)},')
    linhas.append('        },')
linhas += ['    };', '', '    /// <summary>O centro de cada ponto da matriz (linha a linha, de cima) e o raio.</summary>',
           '    public static readonly Dictionary<string, ((double X, double Y)[] Centros, double Raio)> Pontos = new()', '    {']
for tipo, centros, r in cs_pontos:
    linhas.append(f'        ["{tipo}"] = ({em_px(centros)}, {r * A:.2f}),')
linhas += ['    };', '', '    /// <summary>Os LEDs (o centro, o raio, a cor e o pino que acende cada um).</summary>',
           '    public static readonly Dictionary<string, (double X, double Y, double Raio, string Cor, string Pino)[]> Luzes = new()', '    {']
for tipo, luzes in cs_luzes:
    itens = ', '.join(f'({x * A:.2f}, {y * A:.2f}, {r * A:.2f}, "{cor}", "{nome}")' for x, y, r, cor, nome in luzes)
    linhas.append(f'        ["{tipo}"] = [{itens}],')
linhas += ['    };', '}']
io.open(pa.BASE + 'app/DesenhoDosMostradores.cs', 'w', encoding='utf-8', newline='').write('\n'.join(linhas) + '\n')
print('  app/DesenhoDosMostradores.cs')
