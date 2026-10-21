"""python ferramentas/extrair-svg/pecas-diodos.py

Os diodos (Alpha 24), axiais, com o mesmo passo de pernas do resistor --
redesenhados na Alpha 25 no estilo dos passivos (as primitivas de
placas-arduino.py), no tamanho real de cada encapsulamento:

  - diodo          : o 1N4007 (DO-41: 5,2 x 2,7 mm, epoxi preto, faixa cinza no
                     catodo), o 1N4001, o 1N4004 e o UF4007 (o mesmo DO-41); o
                     1N4148 (DO-35: o vidro vermelho de 3,9 x 1,85 mm, com os
                     dois tarugos e o chip vistos por dentro); o 1N5408
                     (DO-201AD: 9,5 x 5,3 mm) e o 6A10 (R-6: 9,1 x 9,1 mm).
  - diodo-zener    : os 1N47xx de 1 W (DO-41 de vidro, ambar, faixa preta), com o
                     numero impresso -- 1N4728A (3,3 V), 1N4732A (4,7 V),
                     1N4733A (5,1 V), 1N4735A (6,2 V), 1N4739A (9,1 V), 1N4742A
                     (12 V), 1N4744A (15 V) -- e o BZX55C5V1 de 0,5 W (DO-35).
  - diodo-schottky : o 1N5819 e o 1N5817 (DO-41 preto), o 1N5822 e o SB560
                     (DO-201AD), o BAT85 (DO-34, o vidro de 3 mm) e o SS34 de
                     verdade -- SMD (DO-214AC) --, com as pernas soldadas nos
                     terminais para caber na protoboard.

A faixa e o catodo (C), na direita; o anodo (A) na esquerda. O que passa da
caixa (o corpo do 1N5408) vai na sobra do SVG.
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
BASE = pa.BASE
A = 96 / 25.4
L_PX, A_PX = 59, 11                          # a caixa do catalogo
L, CY = 15.645, A_PX / 2 / A                 # o comprimento (mm) e o meio da altura
pecas = []

DEFS = pa.DEFS + '''  <linearGradient id="cilindro" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#000000" stop-opacity="0.28"/><stop offset="0.28" stop-color="#ffffff" stop-opacity="0.22"/><stop offset="0.42" stop-color="#ffffff" stop-opacity="0.05"/><stop offset="0.75" stop-color="#000000" stop-opacity="0.08"/><stop offset="1" stop-color="#000000" stop-opacity="0.35"/></linearGradient>
  <linearGradient id="vidro" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#ffffff" stop-opacity="0.05"/><stop offset="0.25" stop-color="#ffffff" stop-opacity="0.45"/><stop offset="0.45" stop-color="#ffffff" stop-opacity="0.08"/><stop offset="1" stop-color="#000000" stop-opacity="0.25"/></linearGradient>
'''


def documento(nome, sy, d):
    W, H = L_PX, A_PX + 2 * sy
    sobra = f' data-sobra="0 {f(sy)}"' if sy else ''
    svg = (f'<svg xmlns="http://www.w3.org/2000/svg" version="1.1" width="{f(W)}px" height="{f(H)}px"{sobra} '
           f'viewBox="0 0 {f(W)} {f(H)}">\n<defs>\n{DEFS}</defs>\n' + ''.join(d.partes) + '</svg>\n')
    io.open(BASE + f'app/assets/{nome}.svg', 'w', encoding='utf-8', newline='').write(svg)


def perna(d, x0, x1, larg):
    """A perna estanhada: a linha grossa e o brilho de cima."""
    d.linha(x0, CY, x1, CY, '#8e959c', larg, cap='butt')
    d.linha(x0, CY - larg * 0.18, x1, CY - larg * 0.18, '#e9edf0', larg * 0.3, so=0.85, cap='butt')


def novo(diam):
    """O desenho em mm (y para baixo), com a sobra que o corpo pedir."""
    sy = round(max(0.0, diam / 2 + 0.1 - CY) * A + 0.6, 1) if diam / 2 + 0.1 > CY else 0.0
    return Desenho(T(A, 0, 0, A, 0, sy)), sy


def epoxi(nome, comp, diam, perna_d, marca, faixa='#b9bdc3'):
    """O axial de epoxi preto (DO-41, DO-201AD): o cilindro, a faixa e a marcacao."""
    d, sy = novo(diam)
    x0, x1 = (L - comp) / 2, (L + comp) / 2
    perna(d, 0, x0 + 0.3, perna_d)
    perna(d, x1 - 0.3, L, perna_d)
    d.ret(x0, CY - diam / 2, comp, diam, '#1c1d20', rx=diam * 0.18)
    banda = comp * 0.15
    d.ret(x1 - banda - comp * 0.06, CY - diam / 2, banda, diam, faixa)
    d.ret(x0, CY - diam / 2, comp, diam, 'url(#cilindro)', rx=diam * 0.18)
    d.texto((x0 + x1 - banda - comp * 0.06) / 2, CY, marca, min(diam * 0.26, comp * 0.14), '#a9adb3', alinhar='center', peso='normal')
    documento(nome, sy, d)


def vidro(nome, comp, diam, perna_d, cor, marca=None, cor_marca='#2a1606', tam=0.16):
    """O axial de vidro (DO-35, DO-41G): os dois tarugos e o chip vistos por
    dentro, a faixa preta pintada no catodo e o numero impresso."""
    d, sy = novo(diam)
    x0, x1 = (L - comp) / 2, (L + comp) / 2
    perna(d, 0, x0 + 0.2, perna_d)
    perna(d, x1 - 0.2, L, perna_d)
    d.ret(x0, CY - diam / 2, comp, diam, cor, rx=diam * 0.35, op=0.78)
    tarugo = comp * 0.36
    for xa in (x0 + 0.15, x1 - 0.15 - tarugo):                                 # os tarugos de dumet
        d.ret(xa, CY - diam * 0.3, tarugo, diam * 0.6, '#8a4a2a', rx=0.12)
        d.ret(xa, CY - diam * 0.3, tarugo, diam * 0.6, 'url(#cilindro)', rx=0.12)
    d.ret((x0 + x1) / 2 - diam * 0.12, CY - diam * 0.16, diam * 0.24, diam * 0.32, '#3a3c40')   # o chip
    d.ret(x0, CY - diam / 2, comp, diam, 'url(#vidro)', rx=diam * 0.35)
    d.ret(x1 - comp * 0.26, CY - diam / 2, comp * 0.13, diam, '#141416', op=0.92)            # a faixa
    if marca:
        d.texto((x0 + x1 - comp * 0.26) / 2 - 0.1, CY, marca, diam * tam, cor_marca, alinhar='center', peso='bold')
    documento(nome, sy, d)


def sma_com_pernas(nome, marca):
    """O SS34 (DO-214AC, SMD) com as duas pernas soldadas nos terminais."""
    d, sy = novo(2.62)
    cx = L / 2
    perna(d, 0, cx - 1.6, 0.6)
    perna(d, cx + 1.6, L, 0.6)
    for s in (-1, 1):                                                          # os terminais e a solda
        d.ret_c(cx + s * 2.35, CY, 1.3, 1.5, 'url(#metal)', rx=0.1)
        d.circ(cx + s * 2.65, CY, 0.55, pa.ESTANHO)
    d.ret_c(cx, CY, 4.3, 2.6, pa.PRETO_CI, rx=0.18)
    d.ret_c(cx, CY, 4.3, 2.6, 'url(#luz)', rx=0.18)
    d.ret_c(cx + 1.65, CY, 0.4, 2.6, '#9aa1a8')
    d.texto(cx - 0.25, CY, marca, 0.72, pa.MARCA_CI, alinhar='center', peso='normal')
    documento(nome, sy, d)


def guardar(id_peca, descricoes):
    pecas.append({
        'id': id_peca, 'largura': L_PX, 'altura': A_PX,
        'pinos': [{'nome': 'A', 'x': 0, 'y': round(CY * A, 2), 'sinais': [], 'descricao': descricoes[0]},
                  {'nome': 'C', 'x': round(L * A - 0.2, 2), 'y': round(CY * A, 2), 'sinais': [], 'descricao': descricoes[1]}],
        'variantes': [], 'metades': False,
    })


# o comum (o primeiro e o de fabrica, e leva o nome do tipo)
epoxi('diodo', 5.2, 2.7, 0.7, '1N4007')
epoxi('diodo-1n4001', 5.2, 2.7, 0.7, '1N4001')
epoxi('diodo-1n4004', 5.2, 2.7, 0.7, '1N4004')
epoxi('diodo-uf4007', 5.2, 2.7, 0.7, 'UF4007')
vidro('diodo-1n4148', 3.9, 1.85, 0.5, '#c2401e')
epoxi('diodo-1n5408', 9.5, 5.3, 1.2, '1N5408')
epoxi('diodo-6a10', 9.1, 9.1, 1.2, '6A10')                        # o R-6, o cilindro de 9 x 9 mm
guardar('diodo', ['Anodo (A): a corrente entra por aqui', 'Catodo (C, o lado da faixa): a corrente sai por aqui'])

# o zener: os 1N47xx de 1 W (DO-41 de vidro) e o BZX55 de 0,5 W (DO-35)
for marca, sufixo in [('1N4733A', ''), ('1N4728A', '-3v3'), ('1N4732A', '-4v7'), ('1N4735A', '-6v2'),
                      ('1N4739A', '-9v1'), ('1N4742A', '-12v'), ('1N4744A', '-15v')]:
    vidro('diodo-zener' + sufixo, 5.2, 2.7, 0.7, '#e0953e', marca)
vidro('diodo-zener-bzx55-5v1', 3.9, 1.85, 0.5, '#d9762a', 'C5V1', tam=0.32)
guardar('diodo-zener', ['Anodo (A): ligue no GND (no regulador de zener)',
                        'Catodo (C, a faixa): o lado positivo -- de ponta-cabeca ele segura a tensao do zener'])

# o schottky
epoxi('diodo-schottky', 5.2, 2.7, 0.7, '1N5819', faixa='#8e939a')
epoxi('diodo-schottky-1n5817', 5.2, 2.7, 0.7, '1N5817', faixa='#8e939a')
epoxi('diodo-schottky-1n5822', 9.5, 5.3, 1.2, '1N5822', faixa='#8e939a')
sma_com_pernas('diodo-schottky-ss34', 'SS34')
vidro('diodo-schottky-bat85', 3.0, 1.6, 0.5, '#7f9a88')           # o DO-34, o vidro menor
epoxi('diodo-schottky-sb560', 9.5, 5.3, 1.2, 'SB560', faixa='#8e939a')
guardar('diodo-schottky', ['Anodo (A)', 'Catodo (C, a faixa): cai so ~0,3 V (o diodo de protecao de bateria e de fonte)'])

cam = BASE + 'app/assets/pecas.json'
lista = json.load(io.open(cam, encoding='utf-8'))
novos = {p['id'] for p in pecas}
lista = [p for p in lista if p['id'] not in novos] + pecas
lista.sort(key=lambda p: p['id'])
io.open(cam, 'w', encoding='utf-8', newline='').write(json.dumps(lista, ensure_ascii=False, indent=1))
print(f'catalogo: {len(lista)} pecas')
