"""python ferramentas/extrair-svg/pecas-atuadores.py

Os atuadores avulsos, redesenhados na Alpha 25 no estilo das placas (as
primitivas de placas-arduino.py), no tamanho real e vistos de cima:

  - buzzer       : o buzzer de 12 mm (12 x 9,5 mm), o preto dos kits: o furo do
                   som no meio e o "+" moldado no lado do pino +. As duas pernas
                   de metal saem por baixo, 7,6 mm uma da outra (cabem na
                   protoboard), com o pino na ponta.
                   Os nomes sao os do Wokwi: o 1 e o -, o 2 e o +.
  - alto-falante : o alto-falante de 28 mm (8 ohms, 0,5 W): o aro, a borda do
                   cone, o cone e a cupula, com os dois fios (o vermelho e o +)
                   saindo por baixo.
  - ks2e-m-dc5   : o rele DPDT de 21 x 10 mm no padrao DIP-16 (as pernas 1, 4, 6,
                   8, 9, 11, 13 e 16): a bobina numa ponta, o comum, o NC e o NO
                   de cada polo na grade de 0,1"; em cima, o modelo e o esquema
                   de dentro, como vem impresso.
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

DEFS_EXTRA = '''  <radialGradient id="buzzer-topo" cx="0.38" cy="0.34" r="0.75"><stop offset="0" stop-color="#34363b"/><stop offset="0.7" stop-color="#18191c"/><stop offset="1" stop-color="#0c0c0e"/></radialGradient>
  <radialGradient id="cone" cx="0.5" cy="0.5" r="0.5"><stop offset="0.3" stop-color="#16171a"/><stop offset="1" stop-color="#2c2e33"/></radialGradient>
  <radialGradient id="borda-cone" cx="0.5" cy="0.5" r="0.5"><stop offset="0.86" stop-color="#1a1b1f"/><stop offset="0.93" stop-color="#3c3f46"/><stop offset="1" stop-color="#16171a"/></radialGradient>
  <radialGradient id="cupula" cx="0.36" cy="0.32" r="0.8"><stop offset="0" stop-color="#5d626c"/><stop offset="0.6" stop-color="#2a2c31"/><stop offset="1" stop-color="#17181b"/></radialGradient>
  <linearGradient id="aro" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#8d939b"/><stop offset="0.45" stop-color="#3b3e44"/><stop offset="1" stop-color="#202226"/></linearGradient>
'''

pecas = []


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


# =====================================================================
#  Buzzer de 12 mm
# =====================================================================
LB = 12.2
CB = LB / 2
X_MENOS, X_MAIS = CB - 3.8, CB + 3.8                    # as pernas, 7,6 mm uma da outra
AB = LB + 4.4                                           # as pernas passam 4,4 mm do corpo
Y_PONTA_B = AB - 0.5
d = Desenho(T(A, 0, 0, A, 0, 0))
# as duas pernas de metal, saindo de baixo do corpo (o corpo cobre o comeco delas)
for x in (X_MENOS, X_MAIS):
    d.ret(x - 0.32, CB, 0.64, Y_PONTA_B - CB, 'url(#metal-h)', rx=0.12, stroke='#6d737a', sw=0.06)
d.circ(CB, CB, 6.0, 'url(#buzzer-topo)', stroke='#050506', sw=0.15)
d.circ(CB, CB, 5.55, None, '#2c2d32', 0.12)               # a quina do corpo
d.circ(CB, CB, 4.4, None, '#202125', 0.1)                 # o degrau da tampa
d.circ(CB, CB, 1.05, '#040405', stroke='#2a2b30', sw=0.12)  # o furo do som
d.circ(CB - 0.25, CB - 0.3, 0.35, '#151518')
d.texto(X_MAIS - 0.3, CB - 2.4, '+', 1.5, '#4a4c52', alinhar='center', peso='bold')
gravar('buzzer', svg_de(d, LB, AB))
guardar('buzzer', LB, AB, [
    ('1', X_MENOS, Y_PONTA_B, [], 'Negativo (-): no GND'),
    ('2', X_MAIS, Y_PONTA_B, [], 'Positivo (+, a perna mais comprida): no pino que toca (tone(), PWM ou HIGH no ativo)'),
])


# =====================================================================
#  Alto-falante de 28 mm
# =====================================================================
LF, AF = 28.0, 32.6
CF = LF / 2
XS_FIO = [CF - PASSO / 2, CF + PASSO / 2]
Y_PONTA = AF - 0.4
d = Desenho(T(A, 0, 0, A, 0, 0))
# os fios saem de tras do aro, embaixo (o vermelho e o +), com a ponta estanhada
for x, cor, borda in ((XS_FIO[0], '#d62a2a', '#8e1414'), (XS_FIO[1], '#25272b', '#0b0b0c')):
    d.linha(x, 24.0, x, Y_PONTA - 1.4, borda, 0.78, cap='butt')
    d.linha(x, 24.0, x, Y_PONTA - 1.4, cor, 0.58, cap='butt')
    d.linha(x, Y_PONTA - 1.4, x, Y_PONTA, ESTANHO, 0.34)
d.circ(CF, CF, 13.8, 'url(#aro)', stroke='#141518', sw=0.2)                 # o aro de aco
d.circ(CF, CF, 12.9, '#1d1f23', stroke='#0f1012', sw=0.12)
d.circ(CF, CF, 12.4, 'url(#borda-cone)')                                     # a borda (o "surround")
d.circ(CF, CF, 10.9, 'url(#cone)', stroke='#101113', sw=0.1)                 # o cone de papel
for r in (9.2, 7.4):
    d.circ(CF, CF, r, None, '#25272c', 0.18)
d.circ(CF, CF, 4.1, 'url(#cupula)', stroke='#0e0f11', sw=0.15)               # a cupula
gravar('alto-falante', svg_de(d, LF, AF))
guardar('alto-falante', LF, AF, [
    ('+', XS_FIO[0], Y_PONTA, [], 'Positivo (fio vermelho)'),
    ('-', XS_FIO[1], Y_PONTA, [], 'Negativo'),
])


# =====================================================================
#  KS2E-M-DC5 (o rele DPDT no padrao DIP-16)
# =====================================================================
LK, AK = 21.0, 10.0
# da ponta do NO ate a bobina: as pernas 8, 6, 4 e 1 do DIP-16 (e 9, 11, 13, 16 do outro lado)
XS_K = [1.6, 1.6 + 2 * PASSO, 1.6 + 4 * PASSO, 1.6 + 7 * PASSO]
YS_K = [AK / 2 - 3 * PASSO / 2, AK / 2 + 3 * PASSO / 2]
BRANCO = '#e6e7ea'
d = Desenho(T(A, 0, 0, A, 0, 0))
d.ret(0.1, 0.1, LK - 0.2, AK - 0.2, '#1b1c1f', rx=0.5, stroke='#070708', sw=0.15)
d.ret(0.1, 0.1, LK - 0.2, AK - 0.2, 'url(#luz)', rx=0.5)
d.ret(0.7, 0.7, LK - 1.4, AK - 1.4, None, rx=0.35, stroke='#2a2b2f', sw=0.12)   # a tampa selada
d.texto(8.4, 3.6, 'KS2E-M-DC5', 1.3, BRANCO, alinhar='center', peso='bold')
d.texto(8.4, 6.1, '5VDC  2A 30VDC', 0.85, BRANCO, alinhar='center', peso='normal')
# o esquema de dentro, impresso na ponta da bobina
xb = 17.6
d.ret_c(xb, AK / 2, 1.6, 3.0, None, stroke=BRANCO, sw=0.13)
d.linha(xb - 0.8, AK / 2 + 1.5, xb + 0.8, AK / 2 - 1.5, BRANCO, 0.13)
d.linha(xb, 1.5, xb, AK / 2 - 1.5, BRANCO, 0.13)
d.linha(xb, AK / 2 + 1.5, xb, AK - 1.5, BRANCO, 0.13)
gravar('ks2e-m-dc5', svg_de(d, LK, AK))
DESC_K = {
    'NO': 'NO (normalmente aberto) do polo {p}: liga no P{p} so com a bobina energizada',
    'NC': 'NC (normalmente fechado) do polo {p}: ligado no P{p} com a bobina desligada',
    'P': 'P{p}, o comum do polo {p}: vai para o NC{p} desligado e para o NO{p} ligado',
}
pinos_k = []
for linha, polo in ((0, 2), (1, 1)):
    for x, base in zip(XS_K[:3], ['NO', 'NC', 'P']):
        pinos_k.append((f'{base}{polo}', x, YS_K[linha], [], DESC_K[base].format(p=polo)))
    pinos_k.append((f'COIL{polo}', XS_K[3], YS_K[linha], ['power'] if polo == 2 else [],
                    'Bobina (5 V, ~70 mA): 5 V entre o COIL1 e o COIL2 ligam o rele. Precisa de transistor '
                    '(o pino da placa nao aguenta) e de diodo de roda livre'))
guardar('ks2e-m-dc5', LK, AK, pinos_k)


cam = pa.BASE + 'app/assets/pecas.json'
lista = json.load(io.open(cam, encoding='utf-8'))
novos = {p['id'] for p in pecas}
lista = [p for p in lista if p['id'] not in novos] + pecas
lista.sort(key=lambda p: p['id'])
io.open(cam, 'w', encoding='utf-8', newline='').write(json.dumps(lista, ensure_ascii=False, indent=1))
print(f'catalogo: {len(lista)} pecas')
