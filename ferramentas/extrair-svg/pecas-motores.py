"""python ferramentas/extrair-svg/pecas-motores.py

Os motores da Alpha 23:

  - motor-dc        : o motor DC de escovas "130" dos kits (3 a 6 V), visto
                      pela ponta do eixo. No eixo vai um disco branco com uma
                      faixa vermelha -- o alvo de tacometro --, que e a parte
                      que gira na tela. Fios vermelho (+) e preto (-).
  - motor-tt        : o motor amarelo com caixa de reducao 1:48 (o "TT" dos
                      carrinhos), visto pela face larga. Gira o eixo branco
                      de duplo D, que sai na direcao de quem olha.
  - motor-brushless : o brushless de aeromodelo A2212 1000KV (outrunner), visto
                      de cima. Gira o sino; a cruzeta de fixacao e os tres
                      fios (A, B, C) ficam parados.

Cada um sai inteiro (miniatura), "-corpo" (o que fica parado) e "-movel" (o
que gira, no mesmo viewBox). O centro do giro de cada um e impresso no fim,
em fracao da caixa, para a tabela Animadas do editor.

O desenho e simples de proposito, como o das pecas de som da Alpha 19.
"""
import io
import json
import math

BASE = 'C:/Users/seraf/OneDrive/Desktop/SimuladorESP32/'
PX = 96 / 25.4
PASSO = 2.54

pecas = []


def gravar_svg(nome, svg):
    io.open(BASE + f'app/assets/{nome}.svg', 'w', encoding='utf-8', newline='').write(svg)


def guardar(id_peca, larg_mm, alt_mm, pinos):
    pecas.append({
        'id': id_peca,
        'largura': round(larg_mm * PX),
        'altura': round(alt_mm * PX),
        'pinos': [{'nome': n, 'x': round(x * PX, 2), 'y': round(y * PX, 2),
                   'sinais': s, 'descricao': d}
                  for n, x, y, s, d in pinos],
        'variantes': [], 'metades': False,
    })
    print(f'{id_peca}: {round(larg_mm * PX)}x{round(alt_mm * PX)} px, {len(pinos)} pinos')


def cabeca(larg, alt, defs=''):
    return (f'<svg xmlns="http://www.w3.org/2000/svg" version="1.1"\n'
            f'     width="{larg}mm" height="{alt}mm" viewBox="0 0 {larg} {alt}">\n'
            + (f'  <defs>\n{defs}\n  </defs>\n' if defs else ''))


def tres(nome, larg, alt, defs, corpo, movel, cx, cy):
    gravar_svg(nome, f'{cabeca(larg, alt, defs)}{corpo}\n{movel}\n</svg>\n')
    gravar_svg(nome + '-corpo', f'{cabeca(larg, alt, defs)}{corpo}\n</svg>\n')
    gravar_svg(nome + '-movel', f'{cabeca(larg, alt, defs)}{movel}\n</svg>\n')
    print(f'  eixo do giro: ({cx} mm, {cy} mm) = fracao ({cx / larg:.5f}, {cy / alt:.5f})')


def ponta(x, y, cor_borda='#8a6d1f'):
    """A ponta de metal de um fio, onde o fio do circuito encosta."""
    return (f'  <circle cx="{x:.2f}" cy="{y:.2f}" r="0.75" fill="#d9b44a" stroke="{cor_borda}" stroke-width="0.2"/>\n'
            f'  <circle cx="{x:.2f}" cy="{y:.2f}" r="0.32" fill="#2a2a2a"/>')


def fio(d, cor, borda):
    return (f'  <path d="{d}" fill="none" stroke="{borda}" stroke-width="1.15" stroke-linecap="round"/>\n'
            f'  <path d="{d}" fill="none" stroke="{cor}" stroke-width="0.8" stroke-linecap="round"/>')


# =====================================================================
#  Motor DC 130, pela ponta do eixo
# =====================================================================
l130, a130 = 28.0, 30.0
cx130, cy130 = 14.0, 12.5
r130 = 10.0          # o corpo e um cilindro de 20 mm...
meia_chata = 7.5     # ...com duas faces planas, 15 mm de altura
x_arco = math.sqrt(r130 ** 2 - meia_chata ** 2)
recuo = (2.4, -2.4)  # o fundo da lata, para dar profundidade


def estadio(cx, cy):
    return (f'M{cx - x_arco:.3f} {cy - meia_chata:.3f} H{cx + x_arco:.3f} '
            f'A{r130} {r130} 0 0 1 {cx + x_arco:.3f} {cy + meia_chata:.3f} '
            f'H{cx - x_arco:.3f} A{r130} {r130} 0 0 1 {cx - x_arco:.3f} {cy - meia_chata:.3f} Z')


# a lateral da lata: os dois pontos de tangencia na direcao do recuo
t = r130 / math.sqrt(2)
p1 = (cx130 + t, cy130 + t)
p2 = (cx130 - t, cy130 - t)
lateral = (f'M{p1[0]:.3f} {p1[1]:.3f} L{p1[0] + recuo[0]:.3f} {p1[1] + recuo[1]:.3f} '
           f'L{p2[0] + recuo[0]:.3f} {p2[1] + recuo[1]:.3f} L{p2[0]:.3f} {p2[1]:.3f} Z')

x_mais, x_menos = cx130 - PASSO / 2, cx130 + PASSO / 2
y_pino130 = a130 - 1.2

defs130 = '''    <linearGradient id="lata130" x1="0" y1="0" x2="1" y2="1">
      <stop offset="0" stop-color="#e3e6ea"/>
      <stop offset="1" stop-color="#9da3ab"/>
    </linearGradient>'''

corpo130 = f'''
  <!-- os fios saem de tras da lata -->
{fio(f"M{x_mais:.2f} {cy130 + 5:.2f} V{y_pino130:.2f}", "#d23b33", "#7d1f1a")}
{fio(f"M{x_menos:.2f} {cy130 + 5:.2f} V{y_pino130:.2f}", "#34373d", "#7a808a")}
{ponta(x_mais, y_pino130)}
{ponta(x_menos, y_pino130)}
  <text x="{x_mais - 1.3:.2f}" y="{y_pino130 - 1.6:.2f}" fill="#d23b33" font-size="2.2px" text-anchor="end"
        font-family="Verdana" font-weight="bold">+</text>
  <text x="{x_menos + 1.3:.2f}" y="{y_pino130 - 1.6:.2f}" fill="#9aa0a8" font-size="2.6px"
        font-family="Verdana" font-weight="bold">-</text>
  <!-- o fundo e a lateral da lata -->
  <path d="{estadio(cx130 + recuo[0], cy130 + recuo[1])}" fill="#868c94" stroke="#5f656d" stroke-width="0.3"/>
  <path d="{lateral}" fill="#9aa0a8"/>
  <!-- a frente: a tampa com o mancal, os dois furos de fixacao -->
  <path d="{estadio(cx130, cy130)}" fill="url(#lata130)" stroke="#6d737c" stroke-width="0.35"/>
  <path d="{estadio(cx130, cy130)}" fill="none" stroke="#f4f6f8" stroke-width="0.25" transform="translate({cx130 * 0.04:.3f} {cy130 * 0.04:.3f}) scale(0.96)"/>
  <circle cx="{cx130 - 6.6:.2f}" cy="{cy130:.2f}" r="0.65" fill="#4b5058"/>
  <circle cx="{cx130 + 6.6:.2f}" cy="{cy130:.2f}" r="0.65" fill="#4b5058"/>
  <circle cx="{cx130:.2f}" cy="{cy130:.2f}" r="3.0" fill="#b08d57" stroke="#7d6236" stroke-width="0.25"/>
  <text x="{cx130:.2f}" y="{cy130 - 5.0:.2f}" fill="#5d636c" font-size="1.6px" text-anchor="middle"
        font-family="Verdana" font-weight="bold">130</text>'''

# o alvo de tacometro: disco branco, faixa vermelha num raio so (da para
# ver o sentido do giro), e o eixo no meio
movel130 = f'''
  <circle cx="{cx130:.2f}" cy="{cy130:.2f}" r="4.3" fill="#f3f3ef" stroke="#8f8f8a" stroke-width="0.25"/>
  <path d="M{cx130:.2f} {cy130 - 0.7:.2f} H{cx130 + 4.15:.2f} V{cy130 + 0.7:.2f} H{cx130:.2f} Z" fill="#d23b33"/>
  <circle cx="{cx130 + 3.1:.2f}" cy="{cy130:.2f}" r="0.35" fill="#f3f3ef"/>
  <circle cx="{cx130:.2f}" cy="{cy130:.2f}" r="1.05" fill="#c9cdd3" stroke="#7d838c" stroke-width="0.2"/>
  <circle cx="{cx130:.2f}" cy="{cy130:.2f}" r="0.45" fill="#5a6068"/>'''

tres('motor-dc', l130, a130, defs130, corpo130, movel130, cx130, cy130)
guardar('motor-dc', l130, a130, [
    ('+', x_mais, y_pino130, [], 'Fio vermelho. Corrente entrando aqui gira o eixo no sentido horario'),
    ('-', x_menos, y_pino130, [], 'Fio preto. Invertido (+ no terra), o motor gira para o outro lado'),
])


# =====================================================================
#  Motor TT amarelo (caixa de reducao 1:48), pela face larga
# =====================================================================
ltt, att = 74.0, 26.0
sx, sy = 12.0, 13.0          # o eixo de saida
x_fim_caixa = 44.0
y_mais_tt, y_menos_tt = att / 2 - PASSO / 2, att / 2 + PASSO / 2
x_pino_tt = ltt - 1.3

defstt = '''    <linearGradient id="lataTT" x1="0" y1="0" x2="0" y2="1">
      <stop offset="0" stop-color="#d9dde2"/>
      <stop offset="0.45" stop-color="#f2f4f6"/>
      <stop offset="1" stop-color="#8e949c"/>
    </linearGradient>'''

corpott = f'''
  <!-- os fios: dos terminais da tampa ate as pontas -->
{fio(f"M68.4 8.8 C71 8.8 70.4 {y_mais_tt:.2f} {x_pino_tt:.2f} {y_mais_tt:.2f}", "#d23b33", "#7d1f1a")}
{fio(f"M68.4 17.2 C71 17.2 70.4 {y_menos_tt:.2f} {x_pino_tt:.2f} {y_menos_tt:.2f}", "#34373d", "#7a808a")}
{ponta(x_pino_tt, y_mais_tt)}
{ponta(x_pino_tt, y_menos_tt)}
  <!-- o motor (a lata prateada), a tampa branca e os terminais -->
  <rect x="42.5" y="3.0" width="21.0" height="20.0" rx="1.2" fill="url(#lataTT)" stroke="#6d737c" stroke-width="0.3"/>
  <path d="M46 6.2 H62 M46 19.8 H62" stroke="#ffffff" stroke-opacity="0.5" stroke-width="0.35"/>
  <rect x="63.2" y="6.0" width="3.6" height="14.0" rx="1.0" fill="#efefea" stroke="#b1b1aa" stroke-width="0.3"/>
  <rect x="66.6" y="8.2" width="2.2" height="1.2" fill="#c9a24a" stroke="#8a6d1f" stroke-width="0.15"/>
  <rect x="66.6" y="16.6" width="2.2" height="1.2" fill="#c9a24a" stroke="#8a6d1f" stroke-width="0.15"/>
  <circle cx="65.0" cy="8.0" r="0.55" fill="#d23b33"/>
  <!-- a caixa amarela: frente redonda em volta do eixo -->
  <path d="M{sx} 2 H{x_fim_caixa} V24 H{sx} A11 11 0 0 1 {sx} 2 Z" fill="#f2c230" stroke="#b8901a" stroke-width="0.35"/>
  <rect x="19.5" y="4.2" width="{x_fim_caixa - 21.5:.1f}" height="17.6" rx="1.6" fill="#f6cf4f" stroke="#d9a923" stroke-width="0.25"/>
  <rect x="{x_fim_caixa - 1.6:.1f}" y="1.2" width="3.0" height="23.6" rx="0.8" fill="#eab826" stroke="#b8901a" stroke-width="0.3"/>
  <circle cx="30.5" cy="5.6" r="1.35" fill="#5a4a1c" stroke="#b8901a" stroke-width="0.25"/>
  <circle cx="30.5" cy="20.4" r="1.35" fill="#5a4a1c" stroke="#b8901a" stroke-width="0.25"/>
  <circle cx="22.6" cy="{sy}" r="1.5" fill="#e5b322" stroke="#b8901a" stroke-width="0.25"/>
  <text x="34.6" y="{sy + 0.8:.1f}" fill="#8a6a10" font-size="2.3px" text-anchor="middle"
        font-family="Verdana" font-weight="bold">1:48</text>
  <!-- o ressalto do eixo -->
  <circle cx="{sx}" cy="{sy}" r="5.4" fill="#e9b827" stroke="#b8901a" stroke-width="0.3"/>
  <circle cx="{sx}" cy="{sy}" r="3.7" fill="#c99a1b"/>'''

# o eixo branco de duplo D (5,4 mm, 3,7 mm entre as faces), com uma marca
# vermelha num lado so -- o duplo D sozinho e simetrico e esconderia o sentido
r_eixo, meia_face = 2.7, 1.85
y_face = math.sqrt(r_eixo ** 2 - meia_face ** 2)
movelt = f'''
  <path d="M{sx - meia_face:.3f} {sy - y_face:.3f} A{r_eixo} {r_eixo} 0 0 1 {sx + meia_face:.3f} {sy - y_face:.3f}
           V{sy + y_face:.3f} A{r_eixo} {r_eixo} 0 0 1 {sx - meia_face:.3f} {sy + y_face:.3f} Z"
        fill="#f7f7f4" stroke="#a5a59e" stroke-width="0.25"/>
  <circle cx="{sx:.2f}" cy="{sy - 1.25:.2f}" r="0.5" fill="#d23b33"/>
  <circle cx="{sx:.2f}" cy="{sy:.2f}" r="0.42" fill="#c8c8c2"/>'''

tres('motor-tt', ltt, att, defstt, corpott, movelt, sx, sy)
guardar('motor-tt', ltt, att, [
    ('+', x_pino_tt, y_mais_tt, [], 'Fio vermelho. Corrente entrando aqui gira o eixo no sentido horario'),
    ('-', x_pino_tt, y_menos_tt, [], 'Fio preto. Invertido (+ no terra), o motor gira para o outro lado'),
])


# =====================================================================
#  Motor brushless A2212 1000KV, de cima
# =====================================================================
lbl, abl = 36.0, 44.0
bx, by = 18.0, 18.0
r_sino = 13.75
xs_bl = [bx - PASSO, bx, bx + PASSO]
y_pino_bl = abl - 1.3

defsbl = '''    <radialGradient id="sinoBL" cx="0.42" cy="0.38" r="0.7">
      <stop offset="0" stop-color="#4a4d55"/>
      <stop offset="1" stop-color="#1d1f23"/>
    </radialGradient>'''

bracos = ''
for k in range(4):
    ang = math.radians(45 + 90 * k)
    ex, ey = bx + 16.0 * math.cos(ang), by + 16.0 * math.sin(ang)
    bracos += (f'  <path d="M{bx:.2f} {by:.2f} L{ex:.2f} {ey:.2f}" stroke="#aeb4bd" stroke-width="4.2" stroke-linecap="round"/>\n'
               f'  <circle cx="{ex - 1.6 * math.cos(ang):.2f}" cy="{ey - 1.6 * math.sin(ang):.2f}" r="0.8" fill="#4b5058"/>\n')

fios_bl = ''
for i, x in enumerate(xs_bl):
    fios_bl += fio(f'M{bx + (i - 1) * 1.6:.2f} {by + 6:.2f} C{bx + (i - 1) * 1.6:.2f} {by + 18:.2f} {x:.2f} {by + 16:.2f} {x:.2f} {y_pino_bl:.2f}',
                   '#34373d', '#7a808a') + '\n'
pontas_bl = '\n'.join(ponta(x, y_pino_bl) for x in xs_bl)
rotulos_bl = '\n'.join(
    f'  <text x="{x:.2f}" y="{y_pino_bl - 1.5:.2f}" fill="#e6ecf5" font-size="1.5px" text-anchor="middle"\n'
    f'        font-family="Verdana" font-weight="bold">{n}</text>'
    for n, x in zip('ABC', xs_bl))

corpobl = f'''
{fios_bl}{pontas_bl}
{rotulos_bl}
  <!-- a cruzeta de fixacao (parada), por baixo do sino -->
{bracos}  <circle cx="{bx}" cy="{by}" r="{r_sino + 0.6:.2f}" fill="#16171a"/>'''


def janela(ang0, ang1, r0, r1):
    a0, a1 = math.radians(ang0), math.radians(ang1)
    pts = [(bx + r1 * math.cos(a0), by + r1 * math.sin(a0)),
           (bx + r1 * math.cos(a1), by + r1 * math.sin(a1)),
           (bx + r0 * math.cos(a1), by + r0 * math.sin(a1)),
           (bx + r0 * math.cos(a0), by + r0 * math.sin(a0))]
    return (f'M{pts[0][0]:.3f} {pts[0][1]:.3f} A{r1} {r1} 0 0 1 {pts[1][0]:.3f} {pts[1][1]:.3f} '
            f'L{pts[2][0]:.3f} {pts[2][1]:.3f} A{r0} {r0} 0 0 0 {pts[3][0]:.3f} {pts[3][1]:.3f} Z')


janelas = '\n'.join(
    f'  <path d="{janela(c - 34, c + 34, 5.6, 10.6)}" fill="#a8641f" stroke="#0e0f11" stroke-width="0.35"/>\n'
    f'  <path d="M{bx + 8.1 * math.cos(math.radians(c - 20)):.3f} {by + 8.1 * math.sin(math.radians(c - 20)):.3f} '
    f'A8.1 8.1 0 0 1 {bx + 8.1 * math.cos(math.radians(c + 20)):.3f} {by + 8.1 * math.sin(math.radians(c + 20)):.3f}" '
    f'fill="none" stroke="#d58a3c" stroke-width="0.5"/>'
    for c in (-90, 30, 150))
parafusos = '\n'.join(
    f'  <circle cx="{bx + 3.9 * math.cos(math.radians(a)):.3f}" cy="{by + 3.9 * math.sin(math.radians(a)):.3f}" r="0.45" fill="#0e0f11"/>'
    for a in (-30, 90, 210))

# o sino gira inteiro: janelas, parafusos, a marca vermelha e o eixo
movelbl = f'''
  <circle cx="{bx}" cy="{by}" r="{r_sino}" fill="url(#sinoBL)" stroke="#0e0f11" stroke-width="0.4"/>
  <circle cx="{bx}" cy="{by}" r="{r_sino - 0.9:.2f}" fill="none" stroke="#5b5f68" stroke-width="0.35"/>
{janelas}
{parafusos}
  <path d="M{bx:.2f} {by - r_sino + 0.6:.2f} l-0.9 1.7 h1.8 Z" fill="#e2473d"/>
  <text x="{bx:.2f}" y="{by + 11.2:.2f}" fill="#c7ccd4" font-size="1.45px" text-anchor="middle"
        font-family="Verdana" font-weight="bold">A2212 1000KV</text>
  <circle cx="{bx}" cy="{by}" r="2.6" fill="#c9cdd3" stroke="#6d737c" stroke-width="0.3"/>
  <circle cx="{bx}" cy="{by}" r="1.58" fill="#9aa0a8" stroke="#5a6068" stroke-width="0.2"/>'''

tres('motor-brushless', lbl, abl, defsbl, corpobl, movelbl, bx, by)
descricoes_bl = {
    'A': 'Fase A. Os tres fios nao tem polaridade: trocar dois deles inverte o giro',
    'B': 'Fase B. No uso normal os tres vao no ESC, nunca direto na placa',
    'C': 'Fase C. Dois fios trocados de lugar = o motor gira para o outro lado',
}
guardar('motor-brushless', lbl, abl, [
    (n, x, y_pino_bl, [], descricoes_bl[n]) for n, x in zip('ABC', xs_bl)
])


# =====================================================================
#  O rotulo de cada modelo (Alpha 23, fichas de motor)
# =====================================================================
# O desenho e o mesmo -- o editor o aumenta ou diminui pelo diametro da
# ficha --, mas o nome escrito na tampa (com escova) e no sino (sem escova)
# e o do motor escolhido.
ROTULOS_COM_ESCOVA = {
    '130': '130', 'rs-360': 'RS-360', 'rs-380': 'RS-380', 'rs-540': 'RS-540',
    'rs-545': 'RS-545', 'rs-550': 'RS-550', 'rs-775': 'RS-775', 'personalizado': 'DC',
}
ROTULOS_SEM_ESCOVA = {
    '1105-5000': '1105 5000KV', '1404-3800': '1404 3800KV', 'a2212-1000': 'A2212 1000KV',
    'a2212-1400': 'A2212 1400KV', '2216-880': '2216 880KV', '2205-2300': '2205 2300KV',
    '2207-1750': '2207 1750KV', '2306-2450': '2306 2450KV', '2814-900': '2814 900KV',
    '3508-700': '3508 700KV', '3650-3100': '3650 3100KV', 'personalizado': 'BRUSHLESS',
}
assert corpo130.count('>130</text>') == 1 and movelbl.count('>A2212 1000KV</text>') == 1
for id_modelo, rotulo in ROTULOS_COM_ESCOVA.items():
    gravar_svg(f'motor-dc-{id_modelo}-corpo',
               f'{cabeca(l130, a130, defs130)}{corpo130.replace(">130</text>", f">{rotulo}</text>")}\n</svg>\n')
for id_modelo, rotulo in ROTULOS_SEM_ESCOVA.items():
    gravar_svg(f'motor-brushless-{id_modelo}-movel',
               f'{cabeca(lbl, abl, defsbl)}{movelbl.replace(">A2212 1000KV</text>", f">{rotulo}</text>")}\n</svg>\n')
print(f'rotulos: {len(ROTULOS_COM_ESCOVA)} com escova, {len(ROTULOS_SEM_ESCOVA)} sem escova')


cam = BASE + 'app/assets/pecas.json'
d = json.load(io.open(cam, encoding='utf-8'))
novos = {p['id'] for p in pecas}
d = [p for p in d if p['id'] not in novos] + pecas
d.sort(key=lambda p: p['id'])
io.open(cam, 'w', encoding='utf-8', newline='').write(json.dumps(d, ensure_ascii=False, indent=1))

print(f'\ncatalogo: {len(d)} pecas')
