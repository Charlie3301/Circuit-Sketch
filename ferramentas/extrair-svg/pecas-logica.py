"""python ferramentas/extrair-svg/pecas-logica.py

A Logica da Alpha 25, no estilo das placas:

  - as portas ideais (NOT, AND, OR, XOR, NAND, NOR, XNOR), o multiplexador e
    os flip-flops (D, SR, DSR): os simbolos de sempre (o formato ANSI das
    portas, o triangulo do clock nos flip-flops, o Q barrado), com os pinos
    nos mesmos lugares -- sao as portas ideais do Wokwi, sem VCC nem GND;
  - o gerador de clock e o analisador logico: os dois instrumentos do Wokwi,
    agora com cara de instrumento (o analisador com as cores das pontas de
    prova nos oito canais e a tela no mesmo lugar, que o editor desenha);
  - os chips de verdade da familia 74HC, no DIP de 0,3" (19 mm, as fileiras a
    7,62 mm, o chanfro e a bolinha do pino 1): 74HC00 (4 NAND), 74HC02 (4 NOR),
    74HC04 (6 NOT), 74HC08 (4 AND), 74HC32 (4 OR), 74HC86 (4 XOR), 74HC74 (2
    flip-flops D com PRE e CLR) e 74HC157 (4 multiplexadores 2x1, DIP-16), com
    a pinagem das fichas -- e so funcionam com o VCC e o GND ligados.
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
A = 96 / 25.4
PASSO = 2.54
CORPO, CONTORNO, ROTULO, APAGADO = '#263241', '#a9bccd', '#eaf0f5', '#93a6b8'
PERNA = '#c9ced4'

pecas = []


def svg_de(d, L, H):
    w, h = L * A, H * A
    return (f'<svg xmlns="http://www.w3.org/2000/svg" version="1.1" width="{f(w)}px" height="{f(h)}px" '
            f'viewBox="0 0 {f(w)} {f(h)}">\n<defs>\n{pa.DEFS}</defs>\n' + ''.join(d.partes) + '</svg>\n')


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


def perna(d, x0, x1, y):
    """O fio de ligacao de um pino do simbolo (estanhado, como uma perna)."""
    d.ret(min(x0, x1), y - 0.26, abs(x1 - x0), 0.52, PERNA, rx=0.1)


def bolha(d, x, y):
    d.circ(x, y, 0.72, CORPO, stroke=CONTORNO, sw=0.24)


def q_barrado(d, x, y, tam, alinhar):
    """O Q com a barra em cima (a saida invertida, o pino NQ)."""
    d.texto(x, y, 'Q', tam, ROTULO, alinhar=alinhar)
    largura = tam * 0.95
    x0 = x - largura if alinhar.endswith('right') else x - largura / 2 if alinhar == 'center' else x
    d.linha(x0 + 0.05, y - tam * 0.75, x0 + largura - 0.05, y - tam * 0.75, ROTULO, 0.13, cap='butt')


# =====================================================================
#  as portas ideais
# =====================================================================
L, H = caixa(17.73, 12.7)
YA, YB, YQ = 1.5 * PASSO, 3.5 * PASSO, 2.5 * PASSO
XQ = 7 * PASSO                                  # o Q fica 0,05 mm fora da caixa, como sempre esteve
TOPO, BASE = 1.4, 11.3
R_OU = 9.45                                     # o arco de tras do OR (1,4 mm de flecha)


def x_de_tras(y, x_tras):
    """Onde o arco de tras do OR passa na altura y."""
    cx = x_tras - R_OU + 1.4
    return cx + (R_OU ** 2 - (y - YQ) ** 2) ** 0.5


def porta(id_peca, nome, forma, invertida=False, xor=False, entradas=2, tri=False):
    """Uma porta ideal: o corpo ANSI (and, or ou o triangulo), as pernas das
    entradas (A e B, ou A, B e C), a bolha da inversao e, no tri-state, o EN
    entrando por baixo."""
    d = Desenho(T(A, 0, 0, A, 0, 0))
    nomes = ['A', 'B', 'C'][:entradas]
    ys = {1: [YQ], 2: [YA, YB], 3: [YA, YQ, YB]}[entradas]
    if forma == 'and':
        ponta = 13.4
        corpo = [('M', 3.6, TOPO), ('L', ponta - 4.95, TOPO), ('A', 4.95, 0, 1, ponta - 4.95, BASE), ('L', 3.6, BASE), ('Z',)]
        tras = {y: 3.6 for y in ys}
        meio = 7.2
    elif forma == 'or':
        ponta, x_tras = 13.8, 3.0
        corpo = [('M', x_tras, TOPO), ('A', 11.0, 0, 1, ponta, YQ), ('A', 11.0, 0, 1, x_tras, BASE),
                 ('A', R_OU, 0, 0, x_tras, TOPO), ('Z',)]
        tras = {y: x_de_tras(y, x_tras) for y in ys}
        meio = 7.9
    else:   # o triangulo: NOT e os buffers
        ponta = 12.2
        corpo = [('M', 4.0, 1.9), ('L', ponta, YQ), ('L', 4.0, 10.8), ('Z',)]
        tras = {YQ: 4.0}
        meio = 6.6
    for y in ys:
        perna(d, 0, tras[y] + 0.1, y)
    x_saida = ponta + (1.44 if invertida else 0)
    perna(d, x_saida - 0.1, XQ, YQ)
    x_en = 3 * PASSO
    if tri:                                              # o EN sobe ate a borda de baixo do triangulo
        y_borda = 10.8 - (x_en - 4.0) / (ponta - 4.0) * (10.8 - YQ)
        d.ret(x_en - 0.26, y_borda - 0.1, 0.52, H - y_borda + 0.1, PERNA, rx=0.1)
    d.caminho(corpo, CORPO, stroke=CONTORNO, sw=0.24)
    d.caminho(corpo, 'url(#luz)')
    if xor:
        d.caminho([('M', 1.8, BASE), ('A', R_OU, 0, 0, 1.8, TOPO)], None, stroke=CONTORNO, sw=0.24)
    if invertida:
        bolha(d, ponta + 0.72, YQ)
    if tri:
        d.texto(x_en - 0.6, H - 1.1, 'EN', 0.8, APAGADO, alinhar='center-right')
    tam = 1.4 if len(nome) <= 2 else 1.25 if forma != 'not' and len(nome) == 3 else 1.15
    d.texto(meio, YQ, nome, tam, ROTULO, alinhar='center')
    gravar(id_peca, d, L, H)
    lista = ' e '.join([', '.join(nomes[:-1]), nomes[-1]]) if len(nomes) > 1 else nomes[0]
    funcao = {'AND': f'HIGH so com {lista} em HIGH', 'NAND': f'LOW so com {lista} em HIGH',
              'OR': f'HIGH com qualquer um ({lista}) em HIGH', 'NOR': f'HIGH so com {lista} em LOW',
              'XOR': f'HIGH com um numero impar de entradas em HIGH', 'XNOR': f'HIGH com um numero par de entradas em HIGH',
              'NOT': 'o contrario de A', 'BUF': 'o mesmo nivel do A'}[nome]
    if tri:
        funcao = 'o A, com o EN em HIGH; com o EN em LOW a saida fica solta (alta impedancia)'
    pinos = [(n, 0, y, [], f'Entrada {n}') for n, y in zip(nomes, ys)]
    if tri:
        pinos.append(('EN', x_en, H, [], 'Habilita: em HIGH a saida segue o A; em LOW ela se solta'))
    pinos.append(('Q', XQ, YQ, [], f'Saida: {funcao} (porta ideal, sem VCC nem GND: acende um LED pelo resistor)'))
    guardar(id_peca, L, H, pinos)


porta('and-gate', 'AND', 'and')
porta('nand-gate', 'NAND', 'and', invertida=True)
porta('or-gate', 'OR', 'or')
porta('nor-gate', 'NOR', 'or', invertida=True)
porta('xor-gate', 'XOR', 'or', xor=True)
porta('xnor-gate', 'XNOR', 'or', invertida=True, xor=True)
porta('not-gate', 'NOT', 'not', invertida=True, entradas=1)
# as que faltavam: o buffer (o "wokwi-gate-buffer"), o buffer tri-state e as de tres entradas
porta('buffer-gate', 'BUF', 'not', entradas=1)
porta('buffer-tristate', 'BUF', 'not', entradas=1, tri=True)
porta('and-gate-3', 'AND', 'and', entradas=3)
porta('nand-gate-3', 'NAND', 'and', invertida=True, entradas=3)
porta('or-gate-3', 'OR', 'or', entradas=3)
porta('nor-gate-3', 'NOR', 'or', invertida=True, entradas=3)
porta('xor-gate-3', 'XOR', 'or', xor=True, entradas=3)
porta('xnor-gate-3', 'XNOR', 'or', invertida=True, xor=True, entradas=3)


# =====================================================================
#  os blocos: o multiplexador e os flip-flops
# =====================================================================
def bloco(id_peca, titulo, esquerda, direita, descricoes, trapezio=False):
    altura = (max(len(esquerda), len(direita)) + 2) * PASSO
    L, H = caixa(17.73, altura)
    d = Desenho(T(A, 0, 0, A, 0, 0))
    x0, x1 = PASSO, 6 * PASSO
    ys_e = [(i + 1.5) * PASSO for i in range(len(esquerda))]
    ys_d = [(i + 1.5) * PASSO for i in range(len(direita))]
    for y in ys_e:
        perna(d, 0, x0 + 0.6 if trapezio else x0 + 0.1, y)
    for y in ys_d:
        perna(d, x1 - (0.9 if trapezio else 0.1), XQ, y)
    if trapezio:
        forma = [('M', x0 + 0.5, 0.6), ('L', x1 - 0.8, 2.3), ('L', x1 - 0.8, H - 2.3), ('L', x0 + 0.5, H - 0.6), ('Z',)]
    else:
        forma = [('M', x0, 0.8), ('L', x1, 0.8), ('L', x1, H - 0.8), ('L', x0, H - 0.8), ('Z',)]
    d.caminho(forma, CORPO, stroke=CONTORNO, sw=0.24)
    d.caminho(forma, 'url(#luz)')
    for nome, y in zip(esquerda, ys_e):
        if nome == 'CLK':                                   # o triangulo da borda de subida
            d.poli([(x0, y - 0.75), (x0 + 1.05, y), (x0, y + 0.75)], None, stroke=CONTORNO, sw=0.2)
            d.texto(x0 + 1.35, y, 'CLK', 1.1, ROTULO, alinhar='center-left')
        else:
            d.texto(x0 + (1.3 if trapezio else 0.75), y, nome, 1.25, ROTULO, alinhar='center-left')
    for nome, y in zip(direita, ys_d):
        if nome == 'NQ':
            q_barrado(d, x1 - 0.75, y, 1.25, 'center-right')
        else:
            d.texto(x1 - (1.1 if trapezio else 0.75), y, nome, 1.25, ROTULO, alinhar='center-right')
    d.texto((x0 + x1) / 2 + 0.9, H / 2, titulo, 1.05 if len(titulo) <= 6 else 0.8, APAGADO, alinhar='center')
    gravar(id_peca, d, L, H)
    guardar(id_peca, L, H, [(n, 0, y, [], descricoes[n]) for n, y in zip(esquerda, ys_e)]
            + [(n, XQ, y, [], descricoes[n]) for n, y in zip(direita, ys_d)])


bloco('mux-2', 'MUX', ['A', 'B', 'SEL'], ['Q'],
      {'A': 'Entrada 0: vai ao Q com o SEL em LOW', 'B': 'Entrada 1: vai ao Q com o SEL em HIGH',
       'SEL': 'A escolha: LOW passa o A, HIGH passa o B', 'Q': 'Saida (porta ideal: acende um LED pelo resistor)'},
      trapezio=True)
FF = {'D': 'O dado: entra no Q na subida do CLK', 'CLK': 'O relogio: o flip-flop muda na borda de subida',
      'S': 'Set: leva o Q a HIGH', 'R': 'Reset: leva o Q a LOW', 'Q': 'Saida (acende um LED pelo resistor)',
      'NQ': 'Saida invertida (Q barrado)'}
bloco('flip-flop-d', 'FF D', ['D', 'CLK'], ['Q', 'NQ'], FF)
bloco('flip-flop-sr', 'FF SR', ['S', 'R', 'CLK'], ['Q', 'NQ'],
      {**FF, 'S': 'Set (na subida do CLK): leva o Q a HIGH; S e R juntos invertem o Q', 'R': 'Reset (na subida do CLK): leva o Q a LOW'})
bloco('flip-flop-dsr', 'FF DSR', ['D', 'S', 'R', 'CLK'], ['Q', 'NQ'],
      {**FF, 'S': 'Set assincrono: em HIGH, o Q vai a HIGH na hora', 'R': 'Reset assincrono: em HIGH, o Q vai a LOW na hora'})
# os que faltavam: o JK e o T (na borda de subida) e os latches SR e D (sem relogio)
bloco('flip-flop-jk', 'FF JK', ['J', 'CLK', 'K'], ['Q', 'NQ'],
      {**FF, 'J': 'J: com o K em LOW, o Q vai a HIGH na subida do CLK; J e K em HIGH invertem o Q',
       'K': 'K: com o J em LOW, o Q vai a LOW na subida do CLK'})
bloco('flip-flop-t', 'FF T', ['T', 'CLK'], ['Q', 'NQ'],
      {**FF, 'T': 'Toggle: em HIGH, o Q inverte a cada subida do CLK (divide a frequencia por 2)'})
bloco('latch-sr', 'LATCH SR', ['S', 'R'], ['Q', 'NQ'],
      {**FF, 'S': 'Set: em HIGH, o Q vai a HIGH na hora (sem relogio)', 'R': 'Reset: em HIGH, o Q vai a LOW na hora; S e R em HIGH juntos poem Q e Q barrado em LOW'})
bloco('latch-d', 'LATCH D', ['D', 'EN'], ['Q', 'NQ'],
      {**FF, 'D': 'O dado: passa para o Q enquanto o EN estiver em HIGH', 'EN': 'Habilita: em HIGH o Q segue o D; em LOW o Q guarda o ultimo valor'})

# =====================================================================
#  o gerador de clock
# =====================================================================
L, H = caixa(17.73, 10.05)
d = Desenho(T(A, 0, 0, A, 0, 0))
YC = 1.5 * PASSO
perna(d, 15.2, XQ, YC)
d.ret(0.4, 0.5, 14.9, H - 1.0, '#2c3036', rx=0.8, stroke='#0d0e10', sw=0.18)
d.ret(0.4, 0.5, 14.9, H - 1.0, 'url(#luz)', rx=0.8)
d.ret(1.3, 1.4, 9.3, 4.9, '#0b1410', rx=0.4, stroke='#454b53', sw=0.15)    # a telinha
for k in range(1, 4):
    d.linha(1.3 + k * 9.3 / 4, 1.6, 1.3 + k * 9.3 / 4, 6.1, '#16261d', 0.08)
onda = [(1.8, 5.2), (3.3, 5.2), (3.3, 2.5), (5.6, 2.5), (5.6, 5.2), (7.9, 5.2), (7.9, 2.5), (10.1, 2.5)]
d.poli(onda, None, stroke='#7fe06e', sw=0.28, fechado=False)
d.texto(5.95, 7.9, '0,5 a 20 Hz', 0.85, APAGADO, alinhar='center')
d.texto(12.95, YC, 'CLK', 1.05, ROTULO, alinhar='center')
d.circ(12.95, 6.6, 1.55, '#1b1c1f', stroke='#55595f', sw=0.15)              # o botao da frequencia
d.circ(12.95, 6.6, 1.15, 'url(#plastico)')
d.linha(12.95, 6.6, 12.95, 5.55, '#e8e8e4', 0.22)
gravar('clock-generator', d, L, H)
guardar('clock-generator', L, H, [('CLK', XQ, YC, [], 'A onda quadrada (a frequencia e o controle da peca, de 0,5 a 20 Hz)')])

# =====================================================================
#  o analisador logico (a tela fica onde o editor desenha as ondas)
# =====================================================================
L, H = caixa(60.85, 28.05)
TELA = (0.2083, 0.1091, 0.75, 0.7818)           # EditorCircuito.TelaDoAnalisador
TX, TY, TL, TA = TELA[0] * L, TELA[1] * H, TELA[2] * L, TELA[3] * H
CORES_DAS_PONTAS = ['#1d1d1f', '#7a4a1e', '#d0312d', '#f07c1e', '#f2d22e', '#2e9a3e', '#2a62c9', '#7a3fb0', '#9a9ea4']
CANAIS = ['D0', 'D1', 'D2', 'D3', 'D4', 'D5', 'D6', 'D7', 'GND']
YS_AL = [3.05 + k * PASSO for k in range(9)]
d = Desenho(T(A, 0, 0, A, 0, 0))
d.ret(0.3, 0.3, L - 0.6, H - 0.6, '#2b2e34', rx=1.4, stroke='#0c0d0f', sw=0.2)
d.ret(0.3, 0.3, L - 0.6, H - 0.6, 'url(#luz)', rx=1.4)
d.ret(0.3, YS_AL[0] - 1.6, 2.7, YS_AL[-1] - YS_AL[0] + 3.2, '#16171a', rx=0.3)      # a barra de entrada
for k, (nome, y) in enumerate(zip(CANAIS, YS_AL)):
    d.ret(0, y - 0.32, 2.4, 0.64, 'url(#metal)')
    d.ret(3.4, y - 0.6, 1.2, 1.2, CORES_DAS_PONTAS[k], rx=0.2, stroke='#55595f', sw=0.08)
    d.texto(5.2, y, nome, 1.1, ROTULO, alinhar='center-left')
d.ret(TX - 0.6, TY - 0.6, TL + 1.2, TA + 1.2, '#15171b', rx=0.6)
d.ret(TX, TY, TL, TA, '#090d11', rx=0.3)
for k in range(1, 8):
    d.linha(TX + 0.4, TY + k * TA / 8, TX + TL - 0.4, TY + k * TA / 8, '#18222b', 0.1, cap='butt')
for k in range(1, 10):
    d.linha(TX + k * TL / 10, TY + 0.3, TX + k * TL / 10, TY + TA - 0.3, '#121a21', 0.08, cap='butt')
d.texto(TX + TL / 2, (TY + TA + H - 0.3) / 2, 'ANALISADOR LÓGICO · 8 CANAIS', 0.95, APAGADO, alinhar='center')
gravar('logic-analyzer', d, L, H)
guardar('logic-analyzer', L, H, [(n, 0, y, ['power'] if n == 'GND' else [],
                                  'GND: o terra do circuito medido' if n == 'GND' else f'Canal {n[1]}: desenha o nivel do fio na tela')
                                 for n, y in zip(CANAIS, YS_AL)])

# =====================================================================
#  os chips 74HC, no DIP de 0,3"
# =====================================================================


def dip(nome, n, marca, pinos):
    """O DIP preto de n pinos: o pino 1 embaixo a esquerda (o chanfro a
    esquerda), dai no sentido anti-horario, como na ficha vista de cima."""
    por_lado = n // 2
    comp = 19.05 if n == 14 else 19.3                     # o corpo do N (PDIP) da ficha
    L, H = caixa(comp + 0.6, 9.2)
    xs = [L / 2 + (k - (por_lado - 1) / 2) * PASSO for k in range(por_lado)]
    ys = [H / 2 - 7.62 / 2, H / 2 + 7.62 / 2]
    d = Desenho(T(A, 0, 0, A, 0, 0))
    for x in xs:                                          # as pernas: o ombro largo para fora do corpo
        for y0, y1 in ((ys[0] - 0.45, H / 2 - 3.1), (H / 2 + 3.1, ys[1] + 0.45)):
            d.ret(x - 0.76, min(y0, y1), 1.52, abs(y1 - y0), 'url(#metal-h)', rx=0.12, stroke='#7c838b', sw=0.06)
    d.ret_c(L / 2, H / 2, comp, 6.35, '#1d1e21', rx=0.35, stroke='#08090a', sw=0.12)
    d.ret_c(L / 2, H / 2, comp, 6.35, 'url(#luz)', rx=0.35)
    xe = L / 2 - comp / 2
    d.caminho([('M', xe, H / 2 - 1.1), ('A', 1.1, 0, 1, xe, H / 2 + 1.1), ('Z',)], '#0d0e10')       # o chanfro
    d.circ(xs[0], H / 2 + 1.75, 0.42, '#141517', stroke='#2c2e32', sw=0.08)                          # a bolinha do pino 1
    d.texto(L / 2 + 0.6, H / 2 - 0.55, marca, 1.05, '#c8cbd0', alinhar='center')
    d.texto(L / 2 + 0.6, H / 2 + 1.05, 'E4 2231', 0.7, '#8d9197', alinhar='center', peso='normal')
    furos = [(xs[k], ys[1]) for k in range(por_lado)] + [(xs[por_lado - 1 - k], ys[0]) for k in range(por_lado)]
    gravar(nome, d, L, H)
    assert len(pinos) == n
    guardar(nome, L, H, [(p[0], x, y, p[1], f'Pino {k + 1}: {p[2]}') for k, (p, (x, y)) in enumerate(zip(pinos, furos))])


VCC = ('VCC', ['power'], 'Alimentacao: 2 a 6 V (o 3,3 V ou o 5 V da placa). Sem ele o chip nao funciona')
GND = ('GND', ['power'], 'GND')


def quatro(porta, funcao, saida_primeiro=False):
    """A pinagem das quatro portas de duas entradas (a do 74HC02 comeca pela saida)."""
    def p(k, qual):
        if qual == 'Y':
            return (f'{k}Y', [], f'Saida da porta {k}: {funcao}')
        return (f'{k}{qual}', [], f'Entrada {qual} da porta {k}')
    ordem = ['Y', 'A', 'B'] if saida_primeiro else ['A', 'B', 'Y']
    baixo = [p(1, q) for q in ordem] + [p(2, q) for q in ordem] + [GND]
    cima = [p(3, q) for q in (['A', 'B', 'Y'] if saida_primeiro else ['Y', 'A', 'B'])] + \
           [p(4, q) for q in (['A', 'B', 'Y'] if saida_primeiro else ['Y', 'A', 'B'])] + [VCC]
    return baixo + cima


dip('74hc00', 14, '74HC00N', quatro('NAND', 'nao (A e B)'))
dip('74hc02', 14, '74HC02N', quatro('NOR', 'nao (A ou B)', saida_primeiro=True))
dip('74hc08', 14, '74HC08N', quatro('AND', 'A e B'))
dip('74hc32', 14, '74HC32N', quatro('OR', 'A ou B'))
dip('74hc86', 14, '74HC86N', quatro('XOR', 'A ou B, mas nao os dois'))
nao = [(f'{k}A', [], f'Entrada da porta {k}') for k in range(1, 7)]
sai = [(f'{k}Y', [], f'Saida da porta {k}: o contrario do {k}A') for k in range(1, 7)]
dip('74hc04', 14, '74HC04N', [nao[0], sai[0], nao[1], sai[1], nao[2], sai[2], GND,
                              sai[3], nao[3], sai[4], nao[4], sai[5], nao[5], VCC])


def tres(funcao):
    """As tres portas de tres entradas (74HC10, 11, 27): a pinagem da ficha."""
    def e(k, q):
        return (f'{k}{q}', [], f'Entrada {q} da porta {k}')

    def y(k):
        return (f'{k}Y', [], f'Saida da porta {k}: {funcao}')
    return [e(1, 'A'), e(1, 'B'), e(2, 'A'), e(2, 'B'), e(2, 'C'), y(2), GND,
            y(3), e(3, 'A'), e(3, 'B'), e(3, 'C'), y(1), e(1, 'C'), VCC]


def duas4(funcao):
    """As duas portas de quatro entradas (74HC20, 21): os pinos 3 e 11 sao NC."""
    def e(k, q):
        return (f'{k}{q}', [], f'Entrada {q} da porta {k}')
    nc = ('NC', [], 'Nao ligado por dentro')
    return [e(1, 'A'), e(1, 'B'), nc, e(1, 'C'), e(1, 'D'), (f'1Y', [], f'Saida da porta 1: {funcao}'), GND,
            (f'2Y', [], f'Saida da porta 2: {funcao}'), e(2, 'A'), e(2, 'B'), ('NC.2', [], 'Nao ligado por dentro'),
            e(2, 'C'), e(2, 'D'), VCC]


dip('74hc10', 14, '74HC10N', tres('nao (A e B e C)'))
dip('74hc11', 14, '74HC11N', tres('A e B e C'))
dip('74hc27', 14, '74HC27N', tres('nao (A ou B ou C)'))
dip('74hc20', 14, '74HC20N', duas4('nao (A e B e C e D)'))
dip('74hc21', 14, '74HC21N', duas4('A e B e C e D'))
schmitt = [(f'{k}Y', [], f'Saida da porta {k}: o contrario do {k}A (com histerese: Schmitt-trigger)') for k in range(1, 7)]
dip('74hc14', 14, '74HC14N', [nao[0], schmitt[0], nao[1], schmitt[1], nao[2], schmitt[2], GND,
                              schmitt[3], nao[3], schmitt[4], nao[4], schmitt[5], nao[5], VCC])
dip('74hc7266', 14, '74HC7266N', [('1A', [], 'Entrada A da porta 1'), ('1B', [], 'Entrada B da porta 1'),
                                  ('1Y', [], 'Saida da porta 1: A igual a B'), ('2Y', [], 'Saida da porta 2: A igual a B'),
                                  ('2A', [], 'Entrada A da porta 2'), ('2B', [], 'Entrada B da porta 2'), GND,
                                  ('3A', [], 'Entrada A da porta 3'), ('3B', [], 'Entrada B da porta 3'),
                                  ('3Y', [], 'Saida da porta 3: A igual a B'), ('4Y', [], 'Saida da porta 4: A igual a B'),
                                  ('4A', [], 'Entrada A da porta 4'), ('4B', [], 'Entrada B da porta 4'), VCC])


def buf(k, q):
    return {'OE': (f'{k}OE', [], f'Habilita o buffer {k} (ativo em LOW): em HIGH o {k}Y se solta. Para usar, ligue no GND (solto, flutua)'),
            'A': (f'{k}A', [], f'Entrada do buffer {k}'),
            'Y': (f'{k}Y', [], f'Saida do buffer {k} (o {k}A, ou solta com o {k}OE em HIGH)')}[q]


dip('74hc125', 14, '74HC125N', [buf(1, 'OE'), buf(1, 'A'), buf(1, 'Y'), buf(2, 'OE'), buf(2, 'A'), buf(2, 'Y'), GND,
                                buf(3, 'Y'), buf(3, 'A'), buf(3, 'OE'), buf(4, 'Y'), buf(4, 'A'), buf(4, 'OE'), VCC])


def ff(u, qual):
    return {'CLR': (f'{u}CLR', [], f'Clear do flip-flop {u} (ativo em LOW: leva o {u}Q a LOW). Sem uso, ligue no VCC (solto, flutua)'),
            'D': (f'{u}D', [], f'O dado do flip-flop {u}: entra no {u}Q na subida do {u}CLK'),
            'CLK': (f'{u}CLK', [], f'O relogio do flip-flop {u} (borda de subida)'),
            'PRE': (f'{u}PRE', [], f'Preset do flip-flop {u} (ativo em LOW: leva o {u}Q a HIGH). Sem uso, ligue no VCC (solto, flutua)'),
            'Q': (f'{u}Q', [], f'Saida do flip-flop {u}'),
            'NQ': (f'{u}NQ', [], f'Saida invertida do flip-flop {u} (Q barrado)')}[qual]


dip('74hc74', 14, '74HC74N', [ff(1, 'CLR'), ff(1, 'D'), ff(1, 'CLK'), ff(1, 'PRE'), ff(1, 'Q'), ff(1, 'NQ'), GND,
                              ff(2, 'NQ'), ff(2, 'Q'), ff(2, 'PRE'), ff(2, 'CLK'), ff(2, 'D'), ff(2, 'CLR'), VCC])


def mux(k, qual):
    return {'A': (f'{k}A', [], f'Entrada A do multiplexador {k}: vai ao {k}Y com o S em LOW'),
            'B': (f'{k}B', [], f'Entrada B do multiplexador {k}: vai ao {k}Y com o S em HIGH'),
            'Y': (f'{k}Y', [], f'Saida do multiplexador {k}')}[qual]


dip('74hc157', 16, '74HC157N', [('S', [], 'A escolha dos quatro: LOW passa os A, HIGH passa os B'),
                                mux(1, 'A'), mux(1, 'B'), mux(1, 'Y'), mux(2, 'A'), mux(2, 'B'), mux(2, 'Y'), GND,
                                mux(3, 'Y'), mux(3, 'B'), mux(3, 'A'), mux(4, 'Y'), mux(4, 'B'), mux(4, 'A'),
                                ('G', [], 'Habilita (ativo em LOW): em HIGH as quatro saidas vao a LOW. Para usar, ligue no GND (solto, flutua)'), VCC])


def q595(k):
    return (f'Q{k}', [], f'Saida {k} do latch: o bit {k} do ultimo byte travado pelo STCP (solta com o OE em HIGH)')


# O registrador de deslocamento: os nomes da ficha da Nexperia (os mesmos do Wokwi).
dip('74hc595', 16, '74HC595N', [q595(1), q595(2), q595(3), q595(4), q595(5), q595(6), q595(7), GND,
                                ('Q7S', [], 'Saida serial: o bit que sai do registrador -- ligue no DS do proximo 74HC595 para encadear'),
                                ('MR', [], 'Zera o registrador de deslocamento (ativo em LOW; o latch so muda no proximo STCP). Sem uso, ligue no VCC (solto, flutua)'),
                                ('SHCP', [], 'O relogio do deslocamento: na subida, o DS entra no Q0 e cada bit anda uma casa (o clockPin do shiftOut, o SCK do SPI)'),
                                ('STCP', [], 'O relogio do latch: na subida, os oito bits do registrador vao para as saidas Q0 a Q7 (o latchPin)'),
                                ('OE', [], 'Habilita as saidas (ativo em LOW): em HIGH o Q0 a Q7 se soltam. Para usar, ligue no GND (solto, flutua)'),
                                ('DS', [], 'A entrada serial: o bit que entra na subida do SHCP (o dataPin do shiftOut, o MOSI do SPI)'),
                                q595(0), VCC])

# =====================================================================
cam = pa.BASE + 'app/assets/pecas.json'
dados = json.load(io.open(cam, encoding='utf-8'))
novos = {p['id'] for p in pecas}
dados = [p for p in dados if p['id'] not in novos] + pecas
dados.sort(key=lambda p: p['id'])
io.open(cam, 'w', encoding='utf-8', newline='').write(json.dumps(dados, ensure_ascii=False, indent=1))
print(f'\ncatalogo: {len(dados)} pecas')
