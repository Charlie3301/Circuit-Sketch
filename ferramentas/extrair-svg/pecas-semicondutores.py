"""python ferramentas/extrair-svg/pecas-semicondutores.py

Transistores, MOSFETs e optoacopladores (Alpha 24), redesenhados na Alpha 25
no estilo das placas (as primitivas de placas-arduino.py), no tamanho real, e
com mais modelos e familias. O primeiro modelo de cada lista e o de fabrica (o
SVG leva o nome do tipo); os outros saem como "tipo-modelo":

  - transistor-npn / transistor-pnp : o TO-92 de frente (a face plana, 4,8 x
    4,9 mm), com o numero impresso; as pernas saem a 1,27 mm e abrem para o
    passo da protoboard. Uma familia por PINAGEM: E-B-C (2N2222A, 2N3904,
    2N4401, 2N5551, S8050, S9013, S9014, MPSA13, MPSA42 / 2N2907A, 2N3906,
    2N4403, 2N5401, S8550, S9012, S9015, MPSA92) e C-B-E (transistor-npn-cbe e
    -pnp-cbe: BC547, BC546, BC548, BC549, BC337 / BC557, BC556, BC558, BC327).
  - transistor-npn-potencia / -pnp-potencia : o TO-220, B-C-E (TIP120, TIP122,
    TIP31C, TIP41C, MJE3055T / TIP125, TIP127, TIP32C, TIP42C, MJE2955T).
  - mosfet-n / mosfet-p : o TO-220 (G, D, S) -- o mesmo desenho do 7805
    (pecas-reguladores.py), nas medidas do JEDEC: IRLZ44N, IRF520, IRFZ44N,
    IRF540N, IRF3205, IRL540N, IRLB8721 / IRF9540N, IRF4905, IRF9Z34N.
  - ds18b20 : o sensor de temperatura de 1-Wire no mesmo TO-92 (GND, DQ, VDD).
  - optoacoplador : o PC817 (e o EL817, o LTV-817, o TLP521-1) visto de cima, o
    DIP-4 branco com as fileiras a 7,62 mm: pino 1 (anodo) embaixo a esquerda,
    com a bolinha, e dai no sentido anti-horario: 2 catodo, 3 emissor, 4
    coletor. optoacoplador-4n35 (4N35, 4N25, 4N37): o DIP-6, com o pino 3 sem
    ligacao e a base no 6.

O fusivel e o varistor sao desenhados em pecas-passivos.py; daqui sai so a
ficha deles no catalogo (os pinos).
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
PASSO = 2.54
pecas = []

DEFS_EXTRA = '''  <linearGradient id="niquel" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#e9ecef"/><stop offset="0.5" stop-color="#c2c7cd"/><stop offset="1" stop-color="#959ba2"/></linearGradient>
'''


def documento(nome, l, a, d):
    w, h = l * A, a * A
    svg = (f'<svg xmlns="http://www.w3.org/2000/svg" version="1.1" width="{f(w)}px" height="{f(h)}px" '
           f'viewBox="0 0 {f(w)} {f(h)}">\n<defs>\n{pa.DEFS}{DEFS_EXTRA}</defs>\n' + ''.join(d.partes) + '</svg>\n')
    io.open(BASE + f'app/assets/{nome}.svg', 'w', encoding='utf-8', newline='').write(svg)


def guardar(id_peca, l, a, pinos):
    pecas.append({
        'id': id_peca, 'largura': round(l * A), 'altura': round(a * A),
        'pinos': [{'nome': n, 'x': round(x * A, 2), 'y': round(y * A, 2), 'sinais': [], 'descricao': d} for n, x, y, d in pinos],
        'variantes': [], 'metades': False,
    })


def perna(d, pontos, larg=0.45):
    """A perna estanhada (com o brilho do lado esquerdo), por uma linha quebrada."""
    cmds = [('M', *pontos[0])] + [('L', *p) for p in pontos[1:]]
    d.caminho(cmds, None, '#8e959c', larg, cap='butt')
    d.caminho([(c[0], c[1] - larg * 0.18, c[2]) for c in cmds], None, '#e9edf0', larg * 0.3, cap='butt', so=0.85)


def ponta(d, x, y):
    """O fim da perna, no furo: a ilha dourada (onde o fio encosta)."""
    d.circ(x, y, 0.55, pa.OURO)
    d.circ(x, y, 0.25, '#15171b')


# =====================================================================
#  TO-92 (de frente)
# =====================================================================
l, a = 7.6, 13.0
xs = [l / 2 - PASSO, l / 2, l / 2 + PASSO]
yp = a - 0.6
LARG_92, ALT_92, TOPO_92 = 4.8, 4.9, 0.5


def to92(nome, linhas):
    d = Desenho(T(A, 0, 0, A, 0, 0))
    base = TOPO_92 + ALT_92
    for k, x in enumerate(xs):
        x_saida = l / 2 + (k - 1) * 1.27                    # saem a 1,27 mm e abrem para 2,54
        perna(d, [(x_saida, base - 0.2), (x_saida, base + 1.0), (x, base + 2.4), (x, yp)])
    x0 = l / 2 - LARG_92 / 2
    r = 0.7
    d.caminho([('M', x0, base), ('L', x0, TOPO_92 + r), ('A', r, 0, 1, x0 + r, TOPO_92),
               ('L', x0 + LARG_92 - r, TOPO_92), ('A', r, 0, 1, x0 + LARG_92, TOPO_92 + r),
               ('L', x0 + LARG_92, base), ('Z',)], '#1d1e22', '#0a0a0b', 0.1)
    d.ret(x0, TOPO_92, LARG_92, ALT_92, 'url(#luz)', rx=0.3)
    d.ret(x0 + 0.25, base - 0.45, LARG_92 - 0.5, 0.25, '#2c2e33')            # a rebarba do molde
    for k, (t, tam, cor) in enumerate(linhas):
        d.texto(l / 2, TOPO_92 + 1.35 + k * 1.15, t, tam, cor, alinhar='center', peso='bold' if k < 2 else 'normal')
    for x in xs:
        ponta(d, x, yp)
    documento(nome, l, a, d)


CLARO, LOTE = '#d9dce0', '#9aa0a8'
# (as duas linhas do numero; o primeiro modelo de cada lista e o de fabrica, e leva o nome do tipo)
TO92 = {
    'transistor-npn': [('2n2222a', '2N', '2222A'), ('2n3904', '2N', '3904'), ('2n4401', '2N', '4401'), ('2n5551', '2N', '5551'),
                       ('s8050', 'S8050', 'D'), ('s9013', 'S9013', 'H'), ('s9014', 'S9014', 'C'), ('mpsa13', 'MPSA', '13'),
                       ('mpsa42', 'MPSA', '42')],
    'transistor-pnp': [('2n2907a', '2N', '2907A'), ('2n3906', '2N', '3906'), ('2n4403', '2N', '4403'), ('2n5401', '2N', '5401'),
                       ('s8550', 'S8550', 'D'), ('s9012', 'S9012', 'H'), ('s9015', 'S9015', 'C'), ('mpsa92', 'MPSA', '92')],
    'transistor-npn-cbe': [('bc547', 'BC', '547B'), ('bc546', 'BC', '546B'), ('bc548', 'BC', '548B'), ('bc549', 'BC', '549C'),
                           ('bc337', 'BC', '337-40')],
    'transistor-pnp-cbe': [('bc557', 'BC', '557B'), ('bc556', 'BC', '556B'), ('bc558', 'BC', '558B'), ('bc327', 'BC', '327-40')],
}
for tipo, modelos in TO92.items():
    for i, (modelo, l1, l2) in enumerate(modelos):
        to92(tipo if i == 0 else f'{tipo}-{modelo}', [(l1, 0.85, CLARO), (l2, 0.85, CLARO), ('C 331', 0.62, LOTE)])
desc_npn = {'E': 'Emissor (E): no GND, no NPN que liga uma carga', 'B': 'Base (B): pelo resistor (1 kohm) no pino da placa',
            'C': 'Coletor (C): no - da carga (o relé, o motor, o LED)'}
desc_pnp = {'E': 'Emissor (E): no + da alimentação, no PNP', 'B': 'Base (B): pelo resistor no pino (LOW liga)',
            'C': 'Coletor (C): no + da carga'}
# a ordem das pernas, da esquerda para a direita, com a face plana para a frente
guardar('transistor-npn', l, a, [(n, x, yp, desc_npn[n]) for n, x in zip('EBC', xs)])
guardar('transistor-pnp', l, a, [(n, x, yp, desc_pnp[n]) for n, x in zip('EBC', xs)])
guardar('transistor-npn-cbe', l, a, [(n, x, yp, desc_npn[n]) for n, x in zip('CBE', xs)])
guardar('transistor-pnp-cbe', l, a, [(n, x, yp, desc_pnp[n]) for n, x in zip('CBE', xs)])

# O DS18B20 (a ficha da Maxim): o mesmo TO-92 de frente, a face plana com a marcacao da
# peca (sem o logotipo do fabricante); as pernas na ordem da ficha, GND, DQ e VDD.
to92('ds18b20', [('18B20', 0.85, CLARO), ('2347C4', 0.62, LOTE), ('+346AB', 0.62, LOTE)])
desc_ds = {'GND': 'GND (pino 1)',
           'DQ': 'Dados do 1-Wire (pino 2): no pino da placa, com um resistor de 4,7 kohm para o VCC (o pull-up)',
           'VCC': 'Alimentacao VDD (pino 3): 3,0 a 5,5 V; ligado no GND, o sensor funciona no modo parasita'}
guardar('ds18b20', l, a, [(n, x, yp, desc_ds[n]) for n, x in zip(['GND', 'DQ', 'VCC'], xs)])


# =====================================================================
#  TO-220 (o MOSFET), de frente, a aba para cima -- como o 7805
# =====================================================================
l, a = 10.0, 27.0
xs2 = [l / 2 - PASSO, l / 2, l / 2 + PASSO]
yp2 = a - 0.6


def to220(nome, linhas):
    d = Desenho(T(A, 0, 0, A, 0, 0))
    d.poli([(0.6, 0.0), (l - 0.6, 0.0), (l, 0.6), (l, 5.5), (l - 0.5, 5.5), (l - 0.5, 6.4), (0.5, 6.4),
            (0.5, 5.5), (0.0, 5.5), (0.0, 0.6)], 'url(#niquel)', '#7b8088', 0.1)
    d.circ(l / 2, 2.8, 1.85, '#24262a', stroke='#6f757c', sw=0.15)
    d.circ(l / 2, 2.8, 1.85, None, '#e9ecef', 0.12)
    for x in xs2:
        d.ret_c(x, 16.3, 1.5, 2.2, 'url(#metal-h)')
        d.ret(x - 0.4, 17.3, 0.8, yp2 - 17.3 + 0.5, 'url(#metal-h)')
    d.ret(0.0, 6.2, l, 9.0, '#1d1e22', rx=0.25, stroke='#0a0a0b', sw=0.1)
    d.ret(0.0, 6.2, l, 9.0, 'url(#luz)', rx=0.25)
    for k, (t, tam, cor, peso) in enumerate(linhas):
        d.texto(l / 2, 9.3 + k * 2.0, t, tam, cor, alinhar='center', peso=peso)
    d.circ(1.4, 14.0, 0.35, '#2c2e33')
    for x in xs2:
        d.circ(x, yp2, 0.62, pa.OURO)
        d.circ(x, yp2, 0.3, '#15171b')
    documento(nome, l, a, d)


def marcas(t, lote, data):
    return [(t, 1.25 if len(t) <= 7 else 1.1, '#d9dce0', 'bold'), (lote, 1.0, '#a9aeb5', 'normal'), (data, 0.85, '#8e949c', 'normal')]


TO220 = {
    'mosfet-n': [('irlz44n', 'IRLZ44N'), ('irf520', 'IRF520'), ('irfz44n', 'IRFZ44N'), ('irf540n', 'IRF540N'), ('irf3205', 'IRF3205'),
                 ('irl540n', 'IRL540N'), ('irlb8721', 'IRLB8721')],
    'mosfet-p': [('irf9540n', 'IRF9540N'), ('irf4905', 'IRF4905'), ('irf9z34n', 'IRF9Z34N')],
    'transistor-npn-potencia': [('tip120', 'TIP120'), ('tip122', 'TIP122'), ('tip31c', 'TIP31C'), ('tip41c', 'TIP41C'),
                                ('mje3055t', 'MJE3055T')],
    'transistor-pnp-potencia': [('tip125', 'TIP125'), ('tip127', 'TIP127'), ('tip32c', 'TIP32C'), ('tip42c', 'TIP42C'),
                                ('mje2955t', 'MJE2955T')],
}
for tipo, modelos in TO220.items():
    for i, (modelo, t) in enumerate(modelos):
        to220(tipo if i == 0 else f'{tipo}-{modelo}', marcas(t, 'P420A' if i % 2 == 0 else 'P231B', '71 4K' if i % 2 == 0 else '35 7G'))
guardar('mosfet-n', l, a, [
    ('G', xs2[0], yp2, 'Gate: a tensão aqui liga o canal (o IRLZ44N liga com 3,3 V; o IRF520 pede 10 V). Um resistor de 10 kohm ao GND evita o gate solto'),
    ('D', xs2[1], yp2, 'Dreno: no - da carga'),
    ('S', xs2[2], yp2, 'Source: no GND'),
])
guardar('mosfet-p', l, a, [
    ('G', xs2[0], yp2, 'Gate: liga com o gate ABAIXO do source (~10 V abaixo para ligar inteiro). Um resistor de 10 kohm ao source mantém desligado'),
    ('D', xs2[1], yp2, 'Dreno: no + da carga (a carga vai do dreno ao GND)'),
    ('S', xs2[2], yp2, 'Source: no + da alimentação'),
])
guardar('transistor-npn-potencia', l, a, [
    ('B', xs2[0], yp2, 'Base (B): pelo resistor no pino da placa (o Darlington TIP120 pede pouca corrente: 1 kohm)'),
    ('C', xs2[1], yp2, 'Coletor (C): no - da carga (é também a aba de metal)'),
    ('E', xs2[2], yp2, 'Emissor (E): no GND'),
])
guardar('transistor-pnp-potencia', l, a, [
    ('B', xs2[0], yp2, 'Base (B): pelo resistor no pino (LOW liga)'),
    ('C', xs2[1], yp2, 'Coletor (C): no + da carga (é também a aba de metal)'),
    ('E', xs2[2], yp2, 'Emissor (E): no + da alimentação'),
])


# =====================================================================
#  os optoacopladores no DIP (vistos de cima): o PC817 de 4 pinos e o 4N35 de 6
# =====================================================================
def dip(nome, n, linhas):
    """O DIP branco de n pinos (4 ou 6), as fileiras a 7,62 mm: o pino 1 embaixo
    a esquerda (a bolinha) e dai no sentido anti-horario. Devolve a caixa e os
    furos de cada pino (1 a n)."""
    por_lado = n // 2
    # a caixa em pixels inteiros (o desenho tem que caber nela sem esticar)
    l = round((por_lado * PASSO + 1.5) * A) / A
    a = round(8.6 * A) / A
    xs_ = [l / 2 + (k - (por_lado - 1) / 2) * PASSO for k in range(por_lado)]
    ys_ = [a / 2 - 7.62 / 2, a / 2 + 7.62 / 2]
    corpo_x, corpo_y = por_lado * PASSO - 0.5, 6.5
    d = Desenho(T(A, 0, 0, A, 0, 0))
    for x in xs_:                                          # os ombros das pernas, para fora do corpo
        for y0, y1 in ((ys_[0] - 0.35, a / 2 - corpo_y / 2 + 0.2), (a / 2 + corpo_y / 2 - 0.2, ys_[1] + 0.35)):
            d.ret(x - 0.5, y0, 1.0, y1 - y0, 'url(#metal-h)')
    d.ret_c(l / 2, a / 2, corpo_x, corpo_y, '#eeeae1', rx=0.3, stroke='#a8a296', sw=0.12)
    d.ret_c(l / 2, a / 2, corpo_x, corpo_y, 'url(#luz)', rx=0.3)
    d.circ(xs_[0] - 0.25, ys_[1] - 1.75, 0.38, '#cfc9bd', stroke='#b1aa9d', sw=0.06)   # a marca do pino 1
    for k, (t, tam, cor, peso) in enumerate(linhas):
        d.texto(l / 2, a / 2 - 0.7 + k * 1.4, t, tam, cor, alinhar='center', peso=peso)
    # 1..n/2 embaixo, da esquerda para a direita; n/2+1..n em cima, da direita para a esquerda
    furos = [(xs_[k], ys_[1]) for k in range(por_lado)] + [(xs_[por_lado - 1 - k], ys_[0]) for k in range(por_lado)]
    for x, y in furos:
        ponta(d, x, y)
    documento(nome, l, a, d)
    return l, a, furos


def marca_dip(t, rank):
    return [(t, 0.85 if len(t) <= 6 else 0.75, '#3a3d42', 'bold'), (rank, 0.7, '#5b5f66', 'normal')]


for i, (modelo, t, rank) in enumerate([('pc817', 'PC817', 'C 418'), ('el817', 'EL817', 'C 422'), ('ltv817', 'LTV-817', 'B 409'),
                                       ('tlp521', 'P521', 'GB 4J')]):
    l, a, furos = dip('optoacoplador' if i == 0 else f'optoacoplador-{modelo}', 4, marca_dip(t, rank))
(xa, ya), (xk, yk), (xe, ye), (xc, yc) = furos
guardar('optoacoplador', l, a, [
    ('A', xa, ya, 'Anodo do LED de dentro (pino 1, o da bolinha): pelo resistor no pino da placa'),
    ('K', xk, yk, 'Catodo do LED (pino 2): no GND da placa'),
    ('C', xc, yc, 'Coletor do fototransistor (pino 4, o lado isolado): no - da carga'),
    ('E', xe, ye, 'Emissor do fototransistor (pino 3): no GND do outro circuito')])

for i, (modelo, t, rank) in enumerate([('4n35', '4N35', 'V 418'), ('4n25', '4N25', 'V 409'), ('4n37', '4N37', 'V 422')]):
    l, a, furos = dip('optoacoplador-4n35' if i == 0 else f'optoacoplador-4n35-{modelo}', 6, marca_dip(t, rank))
nomes6 = [('A', 'Anodo do LED de dentro (pino 1, o da bolinha): pelo resistor no pino da placa'),
          ('K', 'Catodo do LED (pino 2): no GND da placa'),
          ('NC', 'Pino 3: sem ligação por dentro'),
          ('E', 'Emissor do fototransistor (pino 4): no GND do outro circuito'),
          ('C', 'Coletor do fototransistor (pino 5, o lado isolado): no - da carga'),
          ('B', 'Base do fototransistor (pino 6): pode ficar solta; um resistor dela ao emissor deixa a resposta mais rápida')]
guardar('optoacoplador-4n35', l, a, [(n, x, y, dsc) for (n, dsc), (x, y) in zip(nomes6, furos)])


# =====================================================================
#  fusivel e varistor: so a ficha (o desenho e de pecas-passivos.py)
# =====================================================================
l, a = 15.645, 4.0
guardar('fusivel', l, a, [('1', 0, a / 2, 'Terminal'), ('2', l - 0.05, a / 2, 'Terminal')])
l, a = 8.0, 12.0
xs4 = [l / 2 - PASSO, l / 2 + PASSO]
guardar('varistor', l, a, [('1', xs4[0], a - 0.3, 'Terminal (sem polaridade)'), ('2', xs4[1], a - 0.3, 'Terminal')])

cam = BASE + 'app/assets/pecas.json'
lista = json.load(io.open(cam, encoding='utf-8'))
novos = {p['id'] for p in pecas}
lista = [p for p in lista if p['id'] not in novos] + pecas
lista.sort(key=lambda p: p['id'])
io.open(cam, 'w', encoding='utf-8', newline='').write(json.dumps(lista, ensure_ascii=False, indent=1))
print(f'catalogo: {len(lista)} pecas')
