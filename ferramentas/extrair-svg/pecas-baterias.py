"""python ferramentas/extrair-svg/pecas-baterias.py

As baterias prontas desenhadas como as de verdade (Alpha 25), vistas de cima,
deitadas, com as medidas de fabrica e o "+" e o "-" na ponta do que sai
delas (os numeros eletricos ficam em app/Baterias.cs):

  - bateria-9v         : a alcalina 6F22/6LR61 (48,5 x 26,5 mm) com o clip
                         de pressao e os fios vermelho e preto;
  - lipo-1s-500        : a LiPo 503035 (35 x 30 mm), a placa de protecao sob
                         a fita kapton e o conector JST-PH de 2 mm;
  - lipo-2s-2200 ...   : os tijolos de 2S, 3S, 4S e 6S no termo-retratil,
                         os fios grossos de silicone ate o XT60 e o plugue
                         de balanceamento JST-XH (uma vez mais o numero de
                         celulas de fios);
  - power-bank         : o de 10000 mAh (98 x 62 mm), os LEDs da carga, o
                         botao e as portas na lateral (os pinos na USB-A);
  - bateria-chumbo-12v : a selada de 12 V 7 Ah (151 x 65 mm), com os
                         terminais faston na tampa.

Nenhuma marca de fabricante: os rotulos sao os de uma bateria generica.
Os suportes de pilhas sao desenhados pelo editor (DesenhoDoSuporteDePilhas.cs):
o desenho muda com as pilhas que estao neles.
"""
import io
import json

BASE = 'C:/Users/seraf/OneDrive/Desktop/SimuladorESP32/'
PX = 96 / 25.4
PASSO = 2.54

pecas = []


def gravar_svg(nome, svg_):
    io.open(BASE + f'app/assets/{nome}.svg', 'w', encoding='utf-8', newline='').write(svg_)


def guardar(id_peca, larg_mm, alt_mm, mais, menos):
    pecas.append({
        'id': id_peca,
        'largura': round(larg_mm * PX),
        'altura': round(alt_mm * PX),
        'pinos': [{'nome': '+', 'x': round(mais[0] * PX, 2), 'y': round(mais[1] * PX, 2), 'sinais': ['power'], 'descricao': 'Positivo'},
                  {'nome': '-', 'x': round(menos[0] * PX, 2), 'y': round(menos[1] * PX, 2), 'sinais': ['power'], 'descricao': 'Negativo (terra)'}],
        'variantes': [], 'metades': False,
    })
    print(f'{id_peca}: {round(larg_mm * PX)}x{round(alt_mm * PX)} px')


def svg(l, a, conteudo, defs=''):
    return (f'<svg xmlns="http://www.w3.org/2000/svg" version="1.1"\n'
            f'     width="{round(l * PX)}px" height="{round(a * PX)}px" viewBox="0 0 {round(l * PX)} {round(a * PX)}">\n'
            + (f'<defs>\n{defs}</defs>\n' if defs else '')
            + f'<g transform="scale({PX:.6f})">\n{conteudo}</g>\n</svg>\n')


def f(v):
    return f'{v:.3f}'


def texto(x, y, t, tam, cor, peso='normal', ancora='middle', rot=None, familia='Arial', estilo='normal', espaco=None):
    tr = f' transform="rotate({rot} {f(x)} {f(y)})"' if rot is not None else ''
    es = f' letter-spacing="{espaco}"' if espaco is not None else ''
    return (f'  <text x="{f(x)}" y="{f(y)}" fill="{cor}" font-size="{tam}px" text-anchor="{ancora}" dominant-baseline="central" '
            f'font-family="{familia}" font-weight="{peso}" font-style="{estilo}"{es}{tr}>{t}</text>\n')


def gradiente(id_, cores, x2=0, y2=1):
    paradas = ''.join(f'<stop offset="{o}" stop-color="{c}"/>' for o, c in cores)
    return f'  <linearGradient id="{id_}" x1="0" y1="0" x2="{x2}" y2="{y2}">{paradas}</linearGradient>\n'


# ------------------------------------------------------------- fios e pontas

def fio(pontos, cor, escuro, grossura, brilho='#ffffff', opacidade_brilho=0.35):
    """Um fio de silicone/PVC: o contorno escuro, a cor e o brilho de cima."""
    d = f'M{f(pontos[0][0])} {f(pontos[0][1])} ' + ' '.join(
        f'C{f(c1[0])} {f(c1[1])} {f(c2[0])} {f(c2[1])} {f(p[0])} {f(p[1])}' if c1 else f'L{f(p[0])} {f(p[1])}'
        for (c1, c2, p) in pontos[1:])
    return (f'  <path d="{d}" fill="none" stroke="{escuro}" stroke-width="{f(grossura)}" stroke-linecap="round"/>\n'
            f'  <path d="{d}" fill="none" stroke="{cor}" stroke-width="{f(grossura * 0.78)}" stroke-linecap="round"/>\n'
            f'  <path d="{d}" fill="none" stroke="{brilho}" stroke-opacity="{opacidade_brilho}" stroke-width="{f(grossura * 0.22)}" '
            f'stroke-linecap="round" transform="translate(0 {f(-grossura * 0.2)})"/>\n')


def reto(x0, y0, x1, y1):
    """Os pontos de um fio que vai de (x0, y0) a (x1, y1) numa curva suave em S."""
    meio = (x0 + x1) / 2
    return [(x0, y0), ((meio, y0), (meio, y1), (x1, y1))]


def ponta(x, y, comprimento, grossura):
    """A ponta do fio decapada e estanhada, ate o pino em x + comprimento."""
    return (f'  <rect x="{f(x)}" y="{f(y - grossura / 2)}" width="{f(comprimento)}" height="{f(grossura)}" rx="{f(grossura / 2)}" '
            f'fill="#b9bec5" stroke="#6f757d" stroke-width="0.12"/>\n'
            f'  <path d="M{f(x + 0.3)} {f(y - grossura * 0.18)} H{f(x + comprimento - 0.3)}" stroke="#eef1f4" stroke-width="{f(grossura * 0.25)}" '
            f'stroke-linecap="round"/>\n')


VERMELHO = ('#d0312d', '#7d1714')
PRETO = ('#2a2b2e', '#0b0b0c')


# ------------------------------------------------------------- conectores

def xt60(x, cy):
    """O XT60 femea (o do lado da bateria), deitado: os fios entram por
    tras (em x), a boca com os dois contatos fica em x + 16. De cima: o
    corpo amarelo de nylon, os chanfros do lado do "-" e o + e o - moldados."""
    c, l = 16.0, 15.5
    y0 = cy - l / 2
    s = (f'  <!-- o XT60 -->\n'
         f'  <path d="M{f(x)} {f(y0 + 0.8)} Q{f(x)} {f(y0)} {f(x + 0.8)} {f(y0)} H{f(x + c - 0.8)} Q{f(x + c)} {f(y0)} {f(x + c)} {f(y0 + 0.8)} '
         f'V{f(y0 + l - 2.6)} L{f(x + c - 2.2)} {f(y0 + l)} H{f(x + 2.2)} L{f(x)} {f(y0 + l - 2.6)} Z" '
         f'fill="url(#xt60)" stroke="#8a6a00" stroke-width="0.25"/>\n'
         # o degrau da trava e a aba de puxar, atras
         f'  <rect x="{f(x + 0.9)}" y="{f(y0 + 0.9)}" width="{f(c * 0.45)}" height="{f(l - 3.2)}" rx="0.6" fill="#f2c200" stroke="#b38f00" stroke-width="0.18"/>\n'
         f'  <path d="M{f(x + c * 0.55)} {f(y0 + 1.0)} V{f(y0 + l - 1.6)}" stroke="#c9a200" stroke-width="0.25"/>\n')
    s += texto(x + c * 0.76, cy - 3.6, '+', 2.6, '#7a5c00', 'bold')
    s += texto(x + c * 0.76, cy + 3.6, '−', 2.6, '#7a5c00', 'bold')
    # os contatos de latao aparecendo na boca
    for y in (cy - 3.6, cy + 3.6):
        s += (f'  <rect x="{f(x + c - 1.1)}" y="{f(y - 1.8)}" width="1.1" height="3.6" fill="#3a2e00"/>\n'
              f'  <rect x="{f(x + c - 0.9)}" y="{f(y - 1.25)}" width="0.9" height="2.5" fill="#d8b04a"/>\n')
    return s


def jst(x, cy, n, passo, profundidade, cor='#f3f1e8', borda='#a8a395'):
    """Um conector JST (PH ou XH) de n vias: os fios entram por tras, em x."""
    largura = (n - 1) * passo + (3.9 if passo < 2.2 else 4.9)
    y0 = cy - largura / 2
    s = (f'  <rect x="{f(x)}" y="{f(y0)}" width="{f(profundidade)}" height="{f(largura)}" rx="0.35" fill="{cor}" stroke="{borda}" stroke-width="0.18"/>\n'
         f'  <rect x="{f(x + profundidade * 0.55)}" y="{f(y0 + 0.4)}" width="{f(profundidade * 0.4)}" height="{f(largura - 0.8)}" rx="0.25" '
         f'fill="none" stroke="{borda}" stroke-width="0.15"/>\n')
    for k in range(n):
        y = cy - (n - 1) * passo / 2 + k * passo
        s += f'  <rect x="{f(x + profundidade - 0.5)}" y="{f(y - 0.45)}" width="0.5" height="0.9" fill="#6d6a60"/>\n'
    return s


# =====================================================================
#  a bateria de 9 V
# =====================================================================
def bateria_9v():
    l, a = 75.0, 27.5
    x0, y0, c, w = 0.5, 0.5, 48.5, 26.5
    cy = y0 + w / 2
    defs = (gradiente('jaqueta', [(0, '#3c4a63'), (0.45, '#26324a'), (1, '#18213a')])
            + gradiente('banda', [(0, '#e9eef3'), (0.5, '#aab3bd'), (1, '#7d8792')])
            + gradiente('clip', [(0, '#2c2d31'), (1, '#121315')]))
    s = '  <!-- o corpo: a jaqueta de metal pintada, com as bordas viradas -->\n'
    s += f'  <rect x="{f(x0)}" y="{f(y0)}" width="{f(c)}" height="{f(w)}" rx="1.6" fill="url(#jaqueta)" stroke="#0d1220" stroke-width="0.3"/>\n'
    s += f'  <rect x="{f(x0 + c - 9.0)}" y="{f(y0)}" width="7.6" height="{f(w)}" fill="url(#banda)"/>\n'
    s += f'  <path d="M{f(x0 + 1.0)} {f(y0 + 0.7)} H{f(x0 + c - 1.0)}" stroke="#ffffff" stroke-opacity="0.25" stroke-width="0.4"/>\n'
    s += f'  <path d="M{f(x0 + 1.2)} {f(y0 + 0.4)} V{f(y0 + w - 0.4)}" stroke="#0a0f1c" stroke-width="0.5"/>\n'
    s += texto(x0 + 15.5, cy - 1.0, '9V', 11.0, '#ffffff', 'bold')
    s += texto(x0 + 15.5, cy + 7.6, 'ALCALINA', 2.6, '#c8d2e4', 'bold')
    s += texto(x0 + 31.5, cy - 5.5, '6LR61', 2.4, '#c8d2e4')
    s += texto(x0 + 31.5, cy - 2.2, '1604A', 2.0, '#8e9bb4')
    # a polaridade, perto dos terminais
    s += texto(x0 + c - 5.2, cy - 6.0, '+', 3.6, '#1d2433', 'bold')
    s += texto(x0 + c - 5.2, cy + 6.0, '−', 3.6, '#1d2433', 'bold')
    # o clip de pressao, encaixado nos terminais
    cx = x0 + c
    s += '  <!-- o clip de pressao -->\n'
    s += (f'  <rect x="{f(cx - 0.6)}" y="{f(y0 + 1.6)}" width="9.4" height="{f(w - 3.2)}" rx="1.4" fill="url(#clip)" stroke="#050506" stroke-width="0.25"/>\n'
          f'  <rect x="{f(cx + 0.6)}" y="{f(y0 + 3.0)}" width="5.6" height="{f(w - 6.0)}" rx="0.9" fill="none" stroke="#3a3b40" stroke-width="0.25"/>\n'
          f'  <path d="M{f(cx + 0.4)} {f(y0 + 2.2)} H{f(cx + 8.0)}" stroke="#4a4b50" stroke-width="0.25"/>\n')
    # os fios saem do meio do clip
    ponta_x = l - 0.6 - 3.0
    my, ny = cy - PASSO / 2, cy + PASSO / 2
    s += fio(reto(cx + 8.4, cy - 1.0, ponta_x, my), *VERMELHO, 1.3)
    s += fio(reto(cx + 8.4, cy + 1.0, ponta_x, ny), *PRETO, 1.3)
    s += ponta(ponta_x, my, 3.0, 0.75) + ponta(ponta_x, ny, 3.0, 0.75)
    gravar_svg('bateria-9v', svg(l, a, s, defs))
    guardar('bateria-9v', l, a, (l - 0.6, my), (l - 0.6, ny))


# =====================================================================
#  a LiPo pequena de uma celula
# =====================================================================
def lipo_1s():
    l, a = 58.0, 30.8
    x0, y0, c, w = 0.4, 0.4, 35.0, 30.0
    cy = y0 + w / 2
    defs = (gradiente('bolsa', [(0, '#e4e7ea'), (0.5, '#c3c8ce'), (1, '#a6adb5')])
            + gradiente('kapton', [(0, '#f0b400'), (1, '#c98d00')], 1, 0))
    s = '  <!-- a bolsa de aluminio, com a dobra selada em volta -->\n'
    s += (f'  <rect x="{f(x0)}" y="{f(y0)}" width="{f(c)}" height="{f(w)}" rx="1.2" fill="url(#bolsa)" stroke="#7d848c" stroke-width="0.25"/>\n'
          f'  <rect x="{f(x0 + 1.4)}" y="{f(y0 + 1.4)}" width="{f(c - 2.8)}" height="{f(w - 2.8)}" rx="0.8" fill="none" stroke="#9aa1a9" stroke-width="0.25"/>\n')
    # o rotulo branco
    s += (f'  <rect x="{f(x0 + 3.2)}" y="{f(y0 + 3.6)}" width="24.0" height="{f(w - 7.2)}" rx="0.6" fill="#f7f8f9" stroke="#c7ccd2" stroke-width="0.15"/>\n'
          f'  <rect x="{f(x0 + 3.2)}" y="{f(y0 + 3.6)}" width="24.0" height="5.2" rx="0.6" fill="#1f5fae"/>\n')
    s += texto(x0 + 15.2, y0 + 6.2, 'Li-Polymer', 2.6, '#ffffff', 'bold')
    s += texto(x0 + 15.2, cy + 0.2, '3.7V 500mAh', 3.0, '#1a1c1f', 'bold')
    s += texto(x0 + 15.2, cy + 4.4, '503035', 2.2, '#4a4e55')
    s += texto(x0 + 15.2, cy + 7.8, '1S1P  1.85Wh', 1.8, '#6a6f77')
    # a placa de protecao, dobrada sob a kapton amarela
    kx = x0 + c - 2.6
    s += (f'  <rect x="{f(kx)}" y="{f(y0 + 3.0)}" width="5.2" height="{f(w - 6.0)}" rx="0.5" fill="url(#kapton)" fill-opacity="0.92" stroke="#9a6c00" stroke-width="0.2"/>\n'
          f'  <rect x="{f(kx + 1.2)}" y="{f(cy - 5.0)}" width="2.4" height="3.0" rx="0.2" fill="#7a5600" fill-opacity="0.45"/>\n'
          f'  <rect x="{f(kx + 1.2)}" y="{f(cy + 2.0)}" width="2.4" height="3.0" rx="0.2" fill="#7a5600" fill-opacity="0.45"/>\n')
    # os fios finos ate o JST-PH
    jx = l - 0.4 - 4.5
    s += fio(reto(kx + 5.0, cy - 1.0, jx, cy - 1.0), *VERMELHO, 0.95)
    s += fio(reto(kx + 5.0, cy + 1.0, jx, cy + 1.0), *PRETO, 0.95)
    s += jst(jx, cy, 2, 2.0, 4.5)
    gravar_svg('lipo-1s-500', svg(l, a, s, defs))
    guardar('lipo-1s-500', l, a, (l - 0.6, cy - 1.0), (l - 0.6, cy + 1.0))


# =====================================================================
#  os tijolos de LiPo
# =====================================================================
def lipo(tipo, celulas, comprimento, largura, mah, c_rate, wrap, faixa, letra):
    x0, y0 = 0.4, 0.4
    cy_xt = y0 + max(9.6, largura * 0.3)
    n_bal = celulas + 1
    larg_bal = (n_bal - 1) * 2.5 + 4.9
    cy_bal = max(cy_xt + 7.75 + 1.2 + larg_bal / 2, y0 + largura - larg_bal / 2 - 1.0)
    a = max(y0 + largura, cy_bal + larg_bal / 2, cy_xt + 7.75) + 0.4
    xt_x = x0 + comprimento + 22.0
    l = xt_x + 16.0 + 0.4
    v = 3.7 * celulas
    defs = (gradiente(f'wrap', [(0, wrap[0]), (0.5, wrap[1]), (1, wrap[2])])
            + gradiente('xt60', [(0, '#ffd83a'), (1, '#e6b400')]))
    s = '  <!-- o termo-retratil, com as dobras nas pontas -->\n'
    s += (f'  <rect x="{f(x0)}" y="{f(y0)}" width="{f(comprimento)}" height="{f(largura)}" rx="2.0" fill="url(#wrap)" stroke="#060607" stroke-width="0.3"/>\n'
          f'  <rect x="{f(x0)}" y="{f(y0)}" width="2.6" height="{f(largura)}" rx="1.6" fill="#000000" fill-opacity="0.28"/>\n'
          f'  <rect x="{f(x0 + comprimento - 2.6)}" y="{f(y0)}" width="2.6" height="{f(largura)}" rx="1.6" fill="#000000" fill-opacity="0.28"/>\n'
          f'  <path d="M{f(x0 + 3.0)} {f(y0 + 0.8)} H{f(x0 + comprimento - 3.0)}" stroke="#ffffff" stroke-opacity="0.22" stroke-width="0.5"/>\n')
    # a faixa colorida e o rotulo
    fx = x0 + comprimento * 0.08
    s += f'  <rect x="{f(fx)}" y="{f(y0)}" width="{f(comprimento * 0.13)}" height="{f(largura)}" fill="{faixa}"/>\n'
    s += texto(fx + comprimento * 0.065, y0 + largura / 2, f'{celulas}S', min(7.0, largura * 0.22), '#1d2b4a' if faixa == '#f2f2f2' else '#ffffff', 'bold', rot=-90)
    tx = x0 + comprimento * 0.25
    s += texto(tx, y0 + largura * 0.30, 'LiPo', min(8.5, largura * 0.26), letra, 'bold', ancora='start', estilo='italic')
    s += texto(tx, y0 + largura * 0.60, f'{v:.1f}V  {mah}mAh', min(4.4, largura * 0.13), letra, 'bold', ancora='start')
    s += texto(tx, y0 + largura * 0.80, f'{c_rate}C  {celulas}S1P', min(3.2, largura * 0.095), letra, ancora='start')
    s += (f'  <rect x="{f(x0 + comprimento - 18.0)}" y="{f(y0 + largura - 7.0)}" width="13.0" height="4.8" rx="0.5" fill="#f2f2f2" fill-opacity="0.9"/>\n')
    s += texto(x0 + comprimento - 11.5, y0 + largura - 4.6, f'{v * mah / 1000:.1f}Wh', 2.2, '#222222', 'bold')
    # os fios grossos (12 AWG) ate o XT60
    sx = x0 + comprimento - 0.6
    s += fio(reto(sx, cy_xt - 2.2, xt_x + 0.6, cy_xt - 3.6), *VERMELHO, 3.4)
    s += fio(reto(sx, cy_xt + 2.2, xt_x + 0.6, cy_xt + 3.6), *PRETO, 3.4)
    s += xt60(xt_x, cy_xt)
    # o balanceamento: um preto e os vermelhos, ate o JST-XH
    bx = x0 + comprimento + 10.0
    for k in range(n_bal):
        yb = cy_bal - (n_bal - 1) * 2.5 / 2 + k * 2.5
        ys = min(y0 + largura - 2.0, max(cy_xt + 4.5, yb))
        cor = PRETO if k == 0 else VERMELHO
        s += fio(reto(sx, ys, bx + 0.4, yb), *cor, 1.05)
    s += jst(bx, cy_bal, n_bal, 2.5, 5.8)
    gravar_svg(tipo, svg(l, a, s, defs))
    guardar(tipo, l, a, (l - 0.6, cy_xt - 3.6), (l - 0.6, cy_xt + 3.6))


# =====================================================================
#  o power bank
# =====================================================================
def power_bank():
    x0, y0, c, w = 0.4, 0.4, 98.0, 62.0
    l, a = x0 + c + 1.0, y0 + w + 0.4
    cy = y0 + w / 2
    defs = (gradiente('carcaca', [(0, '#4a4d54'), (0.5, '#33363c'), (1, '#24262b')])
            + gradiente('painel', [(0, '#3d4047'), (1, '#2b2d32')], 1, 1))
    s = '  <!-- a carcaca de aluminio escovado e o painel de cima -->\n'
    s += (f'  <rect x="{f(x0)}" y="{f(y0)}" width="{f(c)}" height="{f(w)}" rx="7.0" fill="url(#carcaca)" stroke="#111215" stroke-width="0.35"/>\n'
          f'  <rect x="{f(x0 + 2.0)}" y="{f(y0 + 2.0)}" width="{f(c - 4.0)}" height="{f(w - 4.0)}" rx="5.4" fill="url(#painel)" stroke="#1a1b1f" stroke-width="0.25"/>\n'
          f'  <path d="M{f(x0 + 7.0)} {f(y0 + 1.0)} H{f(x0 + c - 7.0)}" stroke="#7d828b" stroke-width="0.4"/>\n')
    s += texto(x0 + 36.0, cy - 4.0, '10000', 11.0, '#e9ebee', 'bold')
    s += texto(x0 + 36.0, cy + 5.4, 'mAh  ·  37Wh', 3.6, '#a9aeb6')
    s += texto(x0 + 36.0, cy + 11.5, 'SAÍDA 5V 2.1A    ENTRADA 5V 2A', 2.4, '#8a9099')
    # os quatro LEDs da carga e o botao
    for k in range(4):
        s += (f'  <circle cx="{f(x0 + c - 13.5)}" cy="{f(cy - 9.0 + k * 3.6)}" r="0.9" fill="#3aa0ff"/>\n'
              f'  <circle cx="{f(x0 + c - 13.5)}" cy="{f(cy - 9.0 + k * 3.6)}" r="1.6" fill="#3aa0ff" fill-opacity="0.18"/>\n')
    s += (f'  <circle cx="{f(x0 + c - 13.5)}" cy="{f(cy + 9.0)}" r="2.6" fill="#2c2e33" stroke="#15161a" stroke-width="0.3"/>\n'
          f'  <circle cx="{f(x0 + c - 13.5)}" cy="{f(cy + 9.0)}" r="1.9" fill="#3a3d43"/>\n')
    # as portas na lateral: a USB-A de saida (os pinos) e a USB-C de entrada
    px = x0 + c
    s += (f'  <rect x="{f(px - 1.0)}" y="{f(cy - 6.25)}" width="1.6" height="12.5" rx="0.3" fill="#c3c8ce" stroke="#6f757d" stroke-width="0.15"/>\n'
          f'  <rect x="{f(px - 0.6)}" y="{f(cy - 5.4)}" width="0.9" height="10.8" fill="#16171a"/>\n'
          f'  <rect x="{f(px - 1.0)}" y="{f(cy + 13.0)}" width="1.4" height="9.0" rx="0.6" fill="#c3c8ce" stroke="#6f757d" stroke-width="0.15"/>\n')
    s += texto(px - 3.2, cy, 'OUT', 1.8, '#7a8089', rot=-90)
    s += texto(px - 3.2, cy + 17.5, 'IN', 1.8, '#7a8089', rot=-90)
    gravar_svg('power-bank', svg(l, a, s, defs))
    guardar('power-bank', l, a, (px, cy - 3.5), (px, cy + 3.5))


# =====================================================================
#  a bateria de chumbo selada
# =====================================================================
def chumbo():
    x0, y0, c, w = 0.4, 0.4, 151.0, 65.0
    l, a = x0 + c + 0.4, y0 + w + 0.4
    cy = y0 + w / 2
    defs = (gradiente('caixa', [(0, '#3a3c41'), (1, '#1f2024')])
            + gradiente('tampa', [(0, '#34363b'), (1, '#26282c')], 1, 1)
            + gradiente('faston', [(0, '#eef1f4'), (1, '#8d949c')], 1, 0))
    s = '  <!-- a caixa de ABS e a tampa soldada -->\n'
    s += (f'  <rect x="{f(x0)}" y="{f(y0)}" width="{f(c)}" height="{f(w)}" rx="2.5" fill="url(#caixa)" stroke="#0c0c0e" stroke-width="0.4"/>\n'
          f'  <rect x="{f(x0 + 2.2)}" y="{f(y0 + 2.2)}" width="{f(c - 4.4)}" height="{f(w - 4.4)}" rx="1.6" fill="url(#tampa)" stroke="#16171a" stroke-width="0.3"/>\n'
          f'  <rect x="{f(x0 + 8.0)}" y="{f(y0 + 9.0)}" width="{f(c - 34.0)}" height="{f(w - 18.0)}" rx="1.2" fill="#2d2f34" stroke="#1b1c20" stroke-width="0.3"/>\n')
    # os seis tampoes das valvulas, em fila
    for k in range(6):
        vx = x0 + 18.0 + k * 19.5
        s += f'  <rect x="{f(vx)}" y="{f(y0 + 4.2)}" width="7.0" height="2.6" rx="1.3" fill="#1d1e22" stroke="#111215" stroke-width="0.2"/>\n'
    s += texto(x0 + 66.9, cy - 6.0, '12V  7Ah', 13.0, '#f0f1f3', 'bold')
    s += texto(x0 + 66.9, cy + 6.0, 'BATERIA SELADA  ·  VRLA', 3.6, '#c9cdd3', 'bold')
    s += texto(x0 + 66.9, cy + 12.0, 'Recarregável  ·  Não abrir  ·  Uso cíclico 14,5–14,9 V', 2.6, '#9da2aa')
    s += texto(x0 + 66.9, cy + 17.0, 'Uso em flutuação 13,6–13,8 V', 2.6, '#9da2aa')
    # os terminais faston (F1, 4,8 mm), na ponta: + vermelho em cima, - preto embaixo
    tx = x0 + c - 12.0
    pinos = []
    for (ty, base, marca, cor) in ((y0 + 12.0, '#b3261e', '+', '#ff6b5e'), (y0 + w - 12.0, '#141416', '−', '#c9cdd3')):
        s += (f'  <rect x="{f(tx - 5.5)}" y="{f(ty - 5.5)}" width="11.0" height="11.0" rx="1.4" fill="{base}" stroke="#08080a" stroke-width="0.3"/>\n'
              f'  <rect x="{f(tx - 3.4)}" y="{f(ty - 3.4)}" width="6.8" height="6.8" rx="0.6" fill="#5d636b"/>\n'
              f'  <rect x="{f(tx - 2.4)}" y="{f(ty - 0.45)}" width="4.8" height="0.9" fill="url(#faston)" stroke="#5a6068" stroke-width="0.12"/>\n'
              f'  <circle cx="{f(tx)}" cy="{f(ty)}" r="0.55" fill="#2a2d31"/>\n')
        s += texto(tx - 10.0, ty, marca, 7.0, cor, 'bold')
        pinos.append((tx, ty))
    gravar_svg('bateria-chumbo-12v', svg(l, a, s, defs))
    guardar('bateria-chumbo-12v', l, a, pinos[0], pinos[1])


bateria_9v()
lipo_1s()
lipo('lipo-2s-2200', 2, 105.0, 34.0, 2200, 25, ('#2f6fd6', '#2257b3', '#173f86'), '#f2f2f2', '#ffffff')
lipo('lipo-3s-2200', 3, 106.0, 34.0, 2200, 30, ('#2b2c30', '#1c1d20', '#101113'), '#e2561e', '#f2f2f2')
lipo('lipo-4s-1500', 4, 72.0, 35.0, 1500, 100, ('#2b2c30', '#1c1d20', '#101113'), '#2fa84f', '#f2f2f2')
lipo('lipo-6s-1300', 6, 75.0, 38.0, 1300, 100, ('#2b2c30', '#1c1d20', '#101113'), '#c8262c', '#f2f2f2')
power_bank()
chumbo()

cam = BASE + 'app/assets/pecas.json'
d = json.load(io.open(cam, encoding='utf-8'))
novos = {p['id'] for p in pecas}
d = [p for p in d if p['id'] not in novos] + pecas
d.sort(key=lambda p: p['id'])
io.open(cam, 'w', encoding='utf-8', newline='').write(json.dumps(d, ensure_ascii=False, indent=1))
print(f'\ncatalogo: {len(d)} pecas')
