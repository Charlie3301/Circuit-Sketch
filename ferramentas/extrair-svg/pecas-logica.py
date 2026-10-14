"""Desenha as pecas de logica e as poe no catalogo.

Portas (NOT, AND, OR, XOR, NAND, NOR, XNOR), multiplexador, flip-flops
(D, SR, DSR) e gerador de clock. Nenhuma vem dos wokwi-elements.

Tudo em pixels de 96 dpi, com os pinos no passo de 0,1".

    python ferramentas/extrair-svg/pecas-logica.py
"""
import io
import json

BASE = 'C:/Users/seraf/OneDrive/Desktop/SimuladorESP32/'
PASSO = 96 / 25.4 * 2.54          # 9,6 px

CORPO = '#243447'
BORDA = '#4E6274'
TRACO = '#C8D4DE'
TEXTO = '#E6EDF3'

pecas = []


def guardar(id_peca, svg, larg, alt, pinos):
    io.open(BASE + f'app/assets/{id_peca}.svg', 'w', encoding='utf-8', newline='').write(svg)
    pecas.append({
        'id': id_peca, 'largura': round(larg), 'altura': round(alt),
        'pinos': [{'nome': n, 'x': round(x, 2), 'y': round(y, 2), 'sinais': [], 'descricao': ''}
                  for n, x, y in pinos],
        'variantes': [], 'metades': False,
    })
    print(f'{id_peca}: {round(larg)}x{round(alt)} px, {len(pinos)} pinos')


def moldura(larg, alt, extra=''):
    return f'''<svg xmlns="http://www.w3.org/2000/svg" version="1.1"
     width="{larg}" height="{alt}" viewBox="0 0 {larg} {alt}"
     font-family="sans-serif" text-anchor="middle">
  {extra}
</svg>
'''


# ---------------------------------------------------------------
# Portas de duas entradas e a NOT.
# ---------------------------------------------------------------
LARG, ALT = 7 * PASSO, 5 * PASSO          # 67,2 x 48
ENTRADA_A, ENTRADA_B = 1.5 * PASSO, 3.5 * PASSO
SAIDA_Y = 2.5 * PASSO
X_CORPO, X_FIM = PASSO, 5 * PASSO


def pernas(com_b=True, bolha=False):
    linhas = [
        f'<path d="M0 {ENTRADA_A} H{X_CORPO}" stroke="{TRACO}" stroke-width="2"/>',
    ]
    if com_b:
        linhas.append(f'<path d="M0 {ENTRADA_B} H{X_CORPO}" stroke="{TRACO}" stroke-width="2"/>')
    else:
        linhas[0] = f'<path d="M0 {SAIDA_Y} H{X_CORPO}" stroke="{TRACO}" stroke-width="2"/>'

    inicio = X_FIM + (5 if bolha else 0)
    linhas.append(f'<path d="M{inicio} {SAIDA_Y} H{LARG}" stroke="{TRACO}" stroke-width="2"/>')
    if bolha:
        linhas.append(f'<circle cx="{X_FIM + 2.6}" cy="{SAIDA_Y}" r="2.8" '
                      f'fill="{CORPO}" stroke="{TRACO}" stroke-width="1.6"/>')
    return '\n  '.join(linhas)


def porta(id_peca, nome, corpo, com_b=True, bolha=False, marca=''):
    svg = moldura(LARG, ALT, f'''
  {corpo}
  {pernas(com_b, bolha)}
  {marca}
  <text x="{(X_CORPO + X_FIM) / 2:.1f}" y="{ALT - 4}" fill="{TEXTO}" font-size="9">{nome}</text>''')

    pinos = ([('A', 0, ENTRADA_A), ('B', 0, ENTRADA_B)] if com_b
             else [('A', 0, SAIDA_Y)]) + [('Q', LARG, SAIDA_Y)]
    guardar(id_peca, svg, LARG, ALT, pinos)


CORPO_AND = (f'<path d="M{X_CORPO} 6 H{X_CORPO + 14} '
             f'a 16 16 0 0 1 0 {ALT - 12} H{X_CORPO} Z" '
             f'fill="{CORPO}" stroke="{BORDA}" stroke-width="1.6"/>')

CORPO_OU = (f'<path d="M{X_CORPO} 6 q 10 {ALT / 2 - 6} 0 {ALT - 12} '
            f'q 18 0 {X_FIM - X_CORPO} -{(ALT - 12) / 2} '
            f'q -{X_FIM - X_CORPO - 18} -{(ALT - 12) / 2} -{18} -{(ALT - 12) / 2} Z" '
            f'fill="{CORPO}" stroke="{BORDA}" stroke-width="1.6"/>')

ARCO_XOR = (f'<path d="M{X_CORPO - 4.5} 6 q 10 {ALT / 2 - 6} 0 {ALT - 12}" '
            f'fill="none" stroke="{BORDA}" stroke-width="1.6"/>')

CORPO_NOT = (f'<path d="M{X_CORPO} 6 L{X_FIM} {ALT / 2} L{X_CORPO} {ALT - 6} Z" '
             f'fill="{CORPO}" stroke="{BORDA}" stroke-width="1.6"/>')

porta('and-gate', 'AND', CORPO_AND)
porta('nand-gate', 'NAND', CORPO_AND, bolha=True)
porta('or-gate', 'OR', CORPO_OU)
porta('nor-gate', 'NOR', CORPO_OU, bolha=True)
porta('xor-gate', 'XOR', CORPO_OU, marca=ARCO_XOR)
porta('xnor-gate', 'XNOR', CORPO_OU, bolha=True, marca=ARCO_XOR)
porta('not-gate', 'NOT', CORPO_NOT, com_b=False, bolha=True)


# ---------------------------------------------------------------
# Caixas: MUX, flip-flops e clock.
# ---------------------------------------------------------------
def caixa(id_peca, titulo, esquerda, direita, larg_passos=7, alt_passos=None, desenho=''):
    alt_passos = alt_passos or max(len(esquerda), len(direita)) + 2
    larg, alt = larg_passos * PASSO, alt_passos * PASSO
    x0, x1 = PASSO, (larg_passos - 1) * PASSO

    partes = [f'<rect x="{x0}" y="{PASSO / 2}" width="{x1 - x0}" height="{alt - PASSO}" '
              f'rx="3" fill="{CORPO}" stroke="{BORDA}" stroke-width="1.6"/>']
    pinos = []

    for i, nome in enumerate(esquerda):
        y = (i + 1.5) * PASSO
        partes.append(f'<path d="M0 {y} H{x0}" stroke="{TRACO}" stroke-width="2"/>')
        partes.append(f'<text x="{x0 + 11}" y="{y + 3.4}" fill="{TEXTO}" font-size="8">{nome}</text>')
        pinos.append((nome, 0, y))

    for i, nome in enumerate(direita):
        y = (i + 1.5) * PASSO
        partes.append(f'<path d="M{x1} {y} H{larg}" stroke="{TRACO}" stroke-width="2"/>')
        partes.append(f'<text x="{x1 - 11}" y="{y + 3.4}" fill="{TEXTO}" font-size="8">{nome}</text>')
        pinos.append((nome, larg, y))

    partes.append(f'<text x="{larg / 2}" y="{alt - 4}" fill="{TEXTO}" font-size="8.5">{titulo}</text>')
    if desenho:
        partes.append(desenho)

    guardar(id_peca, moldura(larg, alt, '\n  '.join(partes)), larg, alt, pinos)


caixa('mux-2', 'MUX 2x1', ['A', 'B', 'SEL'], ['Q'])
caixa('flip-flop-d', 'FF D', ['D', 'CLK'], ['Q', 'NQ'])
caixa('flip-flop-sr', 'FF SR', ['S', 'R', 'CLK'], ['Q', 'NQ'])
caixa('flip-flop-dsr', 'FF DSR', ['D', 'S', 'R', 'CLK'], ['Q', 'NQ'])

onda = (f'<path d="M{1.6 * PASSO} {2.6 * PASSO} h8 v-12 h9 v12 h9 v-12 h8" '
        f'fill="none" stroke="#FFD54F" stroke-width="2"/>')
caixa('clock-generator', 'CLOCK', [], ['CLK'], larg_passos=7, alt_passos=4, desenho=onda)


# ---------------------------------------------------------------
cam = BASE + 'app/assets/pecas.json'
d = json.load(io.open(cam, encoding='utf-8'))
novos = {p['id'] for p in pecas}
d = [p for p in d if p['id'] not in novos] + pecas
d.sort(key=lambda p: p['id'])
io.open(cam, 'w', encoding='utf-8', newline='').write(json.dumps(d, ensure_ascii=False, indent=1))
print(f'\ncatalogo: {len(d)} pecas')


# ---------------------------------------------------------------
# Analisador logico: oito canais e uma tela para as ondas.
# ---------------------------------------------------------------
LARG_AL, ALT_AL = 24 * PASSO, 11 * PASSO
TELA_X, TELA_Y = 5 * PASSO, 1.2 * PASSO
TELA_L, TELA_A = LARG_AL - TELA_X - PASSO, ALT_AL - 2.4 * PASSO

canais = ['D0', 'D1', 'D2', 'D3', 'D4', 'D5', 'D6', 'D7', 'GND']
partes = [f'<rect x="0" y="0" width="{LARG_AL}" height="{ALT_AL}" rx="4" '
          f'fill="#1B2430" stroke="{BORDA}" stroke-width="1.6"/>',
          f'<rect x="{TELA_X}" y="{TELA_Y}" width="{TELA_L}" height="{TELA_A}" '
          f'rx="2" fill="#0A0E14" stroke="#2E3B4A" stroke-width="1"/>']

pinos_al = []
for i, nome in enumerate(canais):
    y = (i + 1.2) * PASSO
    partes.append(f'<path d="M0 {y} H{2.2 * PASSO}" stroke="{TRACO}" stroke-width="2"/>')
    partes.append(f'<text x="{3.4 * PASSO}" y="{y + 3.2}" fill="{TEXTO}" font-size="8">{nome}</text>')
    pinos_al.append((nome, 0, y))

partes.append(f'<text x="{LARG_AL / 2}" y="{ALT_AL - 3}" fill="#7C8B9A" font-size="8">'
              'ANALISADOR LOGICO</text>')

guardar('logic-analyzer', moldura(LARG_AL, ALT_AL, '\n  '.join(partes)),
        LARG_AL, ALT_AL, pinos_al)

cam = BASE + 'app/assets/pecas.json'
d = json.load(io.open(cam, encoding='utf-8'))
d = [p for p in d if p['id'] != 'logic-analyzer'] + [pecas[-1]]
d.sort(key=lambda p: p['id'])
io.open(cam, 'w', encoding='utf-8', newline='').write(json.dumps(d, ensure_ascii=False, indent=1))

print('area da tela: x', TELA_X / LARG_AL, 'y', TELA_Y / ALT_AL,
      'larg', TELA_L / LARG_AL, 'alt', TELA_A / ALT_AL)
