"""python ferramentas/extrair-svg/pecas-rc-a25.py

O aeromodelo da Alpha 25, nas medidas de verdade:

  - radio-controle : o FS-i6 (modo 2) de frente, 174 x 194 mm, pela foto de
                     frente do fabricante retificada (a silhueta com a alca, as
                     placas azuis e prateadas das chaves, o painel e o visor
                     tracados em motores-cad/fs-i6.json); os manches, as chaves
                     SWA/SWB, a tela e a luz sao do editor (EditorRadio.cs), que
                     le as medidas daqui (DesenhoDoRadio.cs). A marca fica de fora;
  - receptor-rc    : o FS-iA6B de cima, 47 x 26,2 mm: o corpo, o bloco do
                     conector com a lista das portas, as duas antenas, o LED e o
                     furo do UPDATE; os pinos na ponta, na ordem impressa
                     (i-BUS, B/VCC, CH6..CH2, PPM/CH1);
  - esc            : o ESC no termo-retratil, nas medidas da linha Skywalker
                     (12 A 38 x 18, 20 A 42 x 25, 30 A 52 x 26, 40 A 65 x 25,
                     60 A 77 x 35 mm), uma variante por corrente e cor;
  - esc-4em1       : o 4-em-1 de drone (41,6 x 40,5 mm, furos de 30,5 mm): os
                     24 MOSFETs em DFN, os quatro MCUs, as ilhas dos motores, as
                     da bateria e o conector SH de 8 vias.
"""
import importlib.util
import io
import json
import math
import os
import sys

AQUI = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, AQUI)
_spec = importlib.util.spec_from_file_location('estilo_a25', os.path.join(AQUI, 'estilo_a25.py'))
E = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(E)

A = E.A
pa = E.pa
mover, rodar, T = E.mover, E.rodar, E.T
OURO, ESTANHO, SERIGRAFIA = E.OURO, E.ESTANHO, E.SERIGRAFIA
PRETO_CI, MARCA_CI = E.PRETO_CI, E.MARCA_CI
PASSO = 2.54
FS = json.load(io.open(os.path.join(AQUI, 'motores-cad', 'fs-i6.json'), encoding='utf-8'))
CATALOGO = {p['id']: p for p in json.load(io.open(pa.BASE + 'app/assets/pecas.json', encoding='utf-8'))}
pecas = []

DEFS_EXTRA = (
    '  <radialGradient id="cupula" cx="0.36" cy="0.3" r="0.78"><stop offset="0" stop-color="#ffffff" '
    'stop-opacity="0.6"/><stop offset="0.32" stop-color="#ffffff" stop-opacity="0.1"/><stop offset="1" '
    'stop-color="#000000" stop-opacity="0.32"/></radialGradient>\n'
    '  <radialGradient id="aco" cx="0.38" cy="0.32" r="0.8"><stop offset="0" stop-color="#f7f8f9"/>'
    '<stop offset="0.55" stop-color="#b8bec5"/><stop offset="1" stop-color="#7a8189"/></radialGradient>\n'
    '  <radialGradient id="poco" cx="0.5" cy="0.45" r="0.55"><stop offset="0" stop-color="#16171a"/>'
    '<stop offset="0.8" stop-color="#202226"/><stop offset="1" stop-color="#3a3d43"/></radialGradient>\n'
    '  <linearGradient id="cromo" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#f4f6f8"/>'
    '<stop offset="0.45" stop-color="#9aa1a8"/><stop offset="0.55" stop-color="#6b7279"/>'
    '<stop offset="1" stop-color="#d9dde1"/></linearGradient>\n'
    '  <linearGradient id="carcaca" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#3a3c41"/>'
    '<stop offset="1" stop-color="#26282c"/></linearGradient>\n'
    '  <linearGradient id="lcd" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#a9c9a6"/>'
    '<stop offset="1" stop-color="#8fb48e"/></linearGradient>\n'
    '  <linearGradient id="retratil" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#ffffff" '
    'stop-opacity="0.35"/><stop offset="0.3" stop-color="#ffffff" stop-opacity="0.05"/><stop offset="0.75" '
    'stop-color="#000000" stop-opacity="0.05"/><stop offset="1" stop-color="#000000" stop-opacity="0.35"/>'
    '</linearGradient>\n')


def gravar(nome, d, L, H):
    s = E.svg(d, L, H).replace('</defs>', DEFS_EXTRA + '</defs>', 1)
    io.open(pa.BASE + f'app/assets/{nome}.svg', 'w', encoding='utf-8', newline='').write(s)


def guardar(id_peca, L, H, pinos, descricoes=None):
    antigos = {q['nome']: q for q in CATALOGO.get(id_peca, {}).get('pinos', [])}
    descricoes = descricoes or {}
    saida = []
    for n, x, y in pinos:
        assert 0 < x < L and 0 < y < H, (id_peca, n, x, y)
        s, t = descricoes.get(n, (antigos.get(n, {}).get('sinais', []), antigos.get(n, {}).get('descricao', '')))
        saida.append({'nome': n, 'x': round(x * A, 2), 'y': round(y * A, 2), 'sinais': s, 'descricao': t})
    faltam = set(antigos) - {q['nome'] for q in saida}
    assert not faltam, (id_peca, 'sem os pinos', faltam)
    pecas.append({'id': id_peca, 'largura': round(L * A), 'altura': round(H * A), 'pinos': saida,
                  'variantes': CATALOGO.get(id_peca, {}).get('variantes', []), 'metades': False})
    print(f'{id_peca}: {round(L * A)}x{round(H * A)} px, {len(saida)} pinos')


def poligono(d, pts, fill, stroke=None, sw=0.0, op=None):
    d.poli(pts, fill, stroke=stroke, sw=sw, op=op)
    d.partes[-1] = d.partes[-1].replace('<path ', '<path stroke-linejoin="round" ', 1)


def aneis(d, aneis_pts, fill, stroke=None, sw=0.0, op=None):
    cmds = []
    for anel in aneis_pts:
        cmds += [('M', *anel[0])] + [('L', *p) for p in anel[1:]] + [('Z',)]
    d.caminho(cmds, fill, stroke, sw, op)
    d.partes[-1] = d.partes[-1].replace('<path ', '<path fill-rule="evenodd" stroke-linejoin="round" ', 1)


def suavizar(pts, n=2):
    for _ in range(n):
        m = len(pts)
        pts = [((pts[i - 1][0] + 2 * pts[i][0] + pts[(i + 1) % m][0]) / 4,
                (pts[i - 1][1] + 2 * pts[i][1] + pts[(i + 1) % m][1]) / 4) for i in range(m)]
    return pts


def suave(pts, passos=10):
    if len(pts) < 3:
        return pts
    P = [pts[0]] + pts + [pts[-1]]
    out = []
    for i in range(1, len(P) - 2):
        p0, p1, p2, p3 = P[i - 1], P[i], P[i + 1], P[i + 2]
        for k in range(passos):
            t = k / passos
            t2, t3 = t * t, t * t * t
            out.append(tuple(0.5 * ((2 * p1[j]) + (-p0[j] + p2[j]) * t + (2 * p0[j] - 5 * p1[j] + 4 * p2[j] - p3[j]) * t2
                                    + (-p0[j] + 3 * p1[j] - 3 * p2[j] + p3[j]) * t3) for j in range(2)))
    out.append(pts[-1])
    return out


def fio(d, pts, cor, larg=1.0, brilho=0.35):
    c = suave(pts)
    d.poli(c, None, stroke='#000000', sw=larg * 1.18, fechado=False, cap='round')
    d.partes[-1] = d.partes[-1].replace('<path ', '<path stroke-linejoin="round" stroke-opacity="0.55" ', 1)
    d.poli(c, None, stroke=cor, sw=larg, fechado=False, cap='round')
    d.partes[-1] = d.partes[-1].replace('<path ', '<path stroke-linejoin="round" ', 1)
    d.poli([(x - larg * 0.18, y - larg * 0.18) for x, y in c], None, stroke='#ffffff', sw=larg * 0.28, fechado=False,
           cap='round')
    d.partes[-1] = d.partes[-1].replace('<path ', f'<path stroke-linejoin="round" stroke-opacity="{brilho}" ', 1)


def texto_girado(d, x, y, t, tam, cor, graus, peso='normal', ancora='middle', fonte='Arial'):
    X, Y = d.t(x, y)
    e = d.t.escala
    t = t.replace('&', '&amp;').replace('<', '&lt;')
    d.partes.append(f'<text transform="translate({pa.f(X)} {pa.f(Y)}) rotate({graus})" x="0" y="{pa.f(tam * 0.45 * e)}" '
                    f'font-family="{fonte}" font-weight="{peso}" font-size="{pa.f(tam * 1.25 * e)}" fill="{cor}" '
                    f'text-anchor="{ancora}">{t}</text>\n')


# =====================================================================
#  FS-i6 (radio-controle), 174 x 194 mm
# =====================================================================
# as medidas que o editor usa (DesenhoDoRadio.cs repete estas)
RADIO = dict(L=174.0, H=194.0, manches=((33.5, 103.0), (140.5, 103.0)), curso=11.0, raio_manche=22.0,
             chaves=((21.5, 65.5), (42.0, 61.5)), liga=(134.9, 175.7), luz=(134.9, 163.2),
             tela=(85.6, 163.3), tela_wh=(68.4, 33.9))


def chave_alavanca(d, x, y, para_baixo, curta=False):
    """A chave de alavanca vista de frente: a porca cromada e a alavanca."""
    d.circ(x + 0.15, y + 0.2, 3.4, '#000000', op=0.35)
    d.circ(x, y, 3.4, 'url(#aco)', stroke='#5d646b', sw=0.12)
    d.circ(x, y, 2.5, '#3b3f45', stroke='#8d949b', sw=0.12)
    s = 1 if para_baixo else -1
    comp = 3.6 if curta else 5.0
    d.ret(x - 1.15, y + (0 if s > 0 else -comp), 2.3, comp, 'url(#cromo)', rx=0.6, stroke='#5d646b', sw=0.1)
    d.ret_c(x, y + s * comp, 2.6, 1.2, 'url(#aco)', rx=0.5, stroke='#5d646b', sw=0.1)


def botao_trapezio(d, cx, cy, w=9.0, h=3.6):
    """Os botoes UP/DOWN/OK/CANCEL: o paralelogramo cinza claro."""
    k = 1.4
    pts = [(cx - w / 2 + k, cy - h / 2), (cx + w / 2, cy - h / 2), (cx + w / 2 - k, cy + h / 2), (cx - w / 2, cy + h / 2)]
    poligono(d, [(x + 0.15, y + 0.2) for x, y in pts], '#000000', op=0.4)
    poligono(d, pts, '#c9cdd2', stroke='#7c838a', sw=0.15)
    poligono(d, pts, 'url(#luz)')


def trim(d, cx, cy, vertical):
    """O trim: a fenda escura com o cursor serrilhado cromado."""
    w, h = (4.2, 13.0) if vertical else (13.0, 4.2)
    d.ret_c(cx, cy, w + 1.6, h + 1.6, '#1d1e21', rx=1.0, stroke='#3a3d43', sw=0.15)
    d.ret_c(cx, cy, w, h, '#0e0f11', rx=0.6)
    cw, ch = (3.4, 5.6) if vertical else (5.6, 3.4)
    d.ret_c(cx, cy, cw, ch, 'url(#cromo)', rx=0.4, stroke='#5d646b', sw=0.1)
    for k in range(-2, 3):
        if vertical:
            d.linha(cx - 1.4, cy + k * 0.95, cx + 1.4, cy + k * 0.95, '#4f555d', 0.22)
        else:
            d.linha(cx + k * 0.95, cy - 1.4, cx + k * 0.95, cy + 1.4, '#4f555d', 0.22)
    # as setinhas da serigrafia
    S = '#8d949b'
    if vertical:
        poligono(d, [(cx - 0.9, cy - h / 2 - 1.6), (cx + 0.9, cy - h / 2 - 1.6), (cx, cy - h / 2 - 2.6)], S)
        poligono(d, [(cx - 0.9, cy + h / 2 + 1.6), (cx + 0.9, cy + h / 2 + 1.6), (cx, cy + h / 2 + 2.6)], S)
    else:
        poligono(d, [(cx - w / 2 - 1.6, cy - 0.9), (cx - w / 2 - 1.6, cy + 0.9), (cx - w / 2 - 2.6, cy)], S)
        poligono(d, [(cx + w / 2 + 1.6, cy - 0.9), (cx + w / 2 + 1.6, cy + 0.9), (cx + w / 2 + 2.6, cy)], S)


def radio():
    L, H = E.caixa(RADIO['L'], RADIO['H'])
    d = E.novo()
    sil = FS['silhueta'][0]
    # a sombra, a carcaca (a alca e o corpo) e o brilho da borda
    aneis(d, [[(x + 0.8, y + 1.2) for x, y in anel] for anel in sil], '#000000', op=0.3)
    aneis(d, sil, 'url(#carcaca)', stroke='#0d0e10', sw=0.4)
    aneis(d, [[(87 + (x - 87) * 0.985, 100 + (y - 100) * 0.985) for x, y in sil[0]]], None, stroke='#55595f', sw=0.35)
    # o grip texturizado dos lados (os dois paineis emborrachados)
    for x0, x1 in ((2.5, 12.0), (162.0, 171.5)):
        d.ret(x0, 96.0, x1 - x0, 64.0, '#2a2c30', rx=3.0, stroke='#1a1b1e', sw=0.2)
        for k in range(30):
            for j in range(4):
                d.circ(x0 + 1.4 + j * 2.2 + (k % 2) * 1.1, 97.5 + k * 2.1, 0.32, '#202226')
    # o friso que contorna a parte de cima do corpo
    d.ret(15.0, 44.6, 144.0, 1.2, '#4a4d53', rx=0.6, op=0.6)

    # as placas das chaves (medidas na foto, a da direita espelhada): o
    # fundo prateado escovado do lado de dentro, o azul com as listras do
    # lado de fora
    def placa(m):
        def X(x):
            return x if m > 0 else 174.0 - x
        borda = [(X(17.6), 53.0), (X(55.2), 53.0), (X(55.8), 71.6), (X(53.4), 73.6)]
        for k in range(1, 12):
            t = k / 12
            borda.append((X(53.4 - t * 41.2), 73.6 + 3.6 * math.sin(math.pi * t) - 2.6 * t))
        borda += [(X(12.2), 71.0), (X(9.0), 65.6)]
        azul = [(X(17.6), 53.0), (X(44.2), 53.0), (X(35.4), 76.4)] + \
               [(x, y) for x, y in borda[4:] if (x < X(35.4) if m > 0 else x > X(35.4))] + [(X(12.2), 71.0), (X(9.0), 65.6)]
        poligono(d, [(x + 0.2, y + 0.3) for x, y in borda], '#000000', op=0.35)
        poligono(d, borda, '#b4b9bf', stroke='#6b7279', sw=0.2)
        poligono(d, azul, '#2b3fc4', stroke='#1b2780', sw=0.15)
        for k in range(5):
            xa = 17.0 + k * 3.6
            d.linha(X(xa + 6.0), 53.6, X(xa - 2.0), 75.6, '#9aa3b0', 0.85, so=0.6)
        poligono(d, borda, 'url(#luz)')
    placa(1)
    placa(-1)
    S = '#e6e9ee'
    for nome, x, y in (('SWA', 27.6, 57.8), ('SWB', 48.4, 57.4), ('SWC', 129.6, 57.4), ('SWD', 150.8, 61.8)):
        d.texto(x, y, nome, 1.15, '#111214', alinhar='center', peso='bold')
    for x, y, posicoes in ((26.6, 63.0, ('1', '2')), (47.2, 62.4, ('1', '2')), (127.0, 62.6, ('1', '2', '3')),
                           (148.6, 67.0, ('1', '2'))):
        for k, t in enumerate(posicoes):
            yy = y + k * 2.4
            seta = [(x + 1.6, yy + 0.7), (x + 3.0, yy + 0.7), (x + 2.3, yy - 0.6)] if k == 0 else \
                   [(x + 1.6, yy - 0.7), (x + 3.0, yy - 0.7), (x + 2.3, yy + 0.6)] if k == len(posicoes) - 1 else \
                   [(x + 1.6, yy - 0.3), (x + 3.0, yy - 0.3), (x + 3.0, yy + 0.3), (x + 1.6, yy + 0.3)]
            poligono(d, seta, '#111214')
            d.texto(x + 4.4, yy, t, 1.0, '#111214', alinhar='center', peso='bold')
    # as chaves SWC (tres posicoes) e SWD, fixas no desenho; SWA e SWB sao do editor
    for (x, y), baixo in (((133.5, 64.5), False), ((153.5, 69.5), False)):
        chave_alavanca(d, x, y, baixo)
    for x, y in RADIO['chaves']:
        d.circ(x + 0.15, y + 0.2, 3.4, '#000000', op=0.35)
        d.circ(x, y, 3.4, 'url(#aco)', stroke='#5d646b', sw=0.12)
        d.circ(x, y, 2.5, '#3b3f45', stroke='#8d949b', sw=0.12)
    # o escudo do meio: o fundo grafite, os dois potenciometros e a placa cromada (sem a marca)
    escudo = [(58.0, 44.5), (116.0, 44.5), (119.0, 48.0), (119.0, 78.0), (114.0, 82.0), (60.0, 82.0), (55.0, 78.0),
              (55.0, 48.0)]
    poligono(d, escudo, '#2a2c31', stroke='#121315', sw=0.3)
    poligono(d, [(x * 0.98 + 87 * 0.02, y * 0.98 + 63 * 0.02) for x, y in escudo], None, stroke='#4a4e55', sw=0.2)
    d.linha(76.0, 53.0, 87.0, 58.0, '#c9cdd2', 0.35)
    d.linha(87.0, 58.0, 98.0, 53.0, '#c9cdd2', 0.35)
    for x, y, nome in ((70.6, 53.8, 'VRA'), (104.4, 54.6, 'VRB')):
        d.circ(x + 0.2, y + 0.3, 8.5, '#000000', op=0.35)
        d.circ(x, y, 8.5, 'url(#cromo)', stroke='#5d646b', sw=0.15)
        d.circ(x, y, 6.6, '#2b2e33', stroke='#6b7279', sw=0.12)
        for k in range(16):
            a = math.radians(k * 22.5)
            d.linha(x + 4.0 * math.cos(a), y + 4.0 * math.sin(a), x + 6.2 * math.cos(a), y + 6.2 * math.sin(a),
                    '#3f434a', 0.35)
        d.circ(x, y, 3.8, 'url(#aco)', stroke='#5d646b', sw=0.1)
        d.linha(x, y - 3.4, x, y - 6.4, '#ffffff', 0.4)
        d.texto(x - 1.0, y + 11.6, nome, 1.15, '#c9cdd2', alinhar='center', peso='bold')
    d.ret(64.0, 66.5, 46.0, 11.5, 'url(#cromo)', rx=3.0, stroke='#5d646b', sw=0.2)
    d.ret(65.6, 68.0, 42.8, 8.5, '#1f2125', rx=2.2)
    # os pocos dos manches: o anel, o fundo e a janela do gimbal, as marcas vermelhas
    for cx, cy in RADIO['manches']:
        d.circ(cx + 0.4, cy + 0.6, 27.5, '#000000', op=0.35)
        d.circ(cx, cy, 27.5, '#1b1c1f', stroke='#45484e', sw=0.4)
        d.circ(cx, cy, 25.0, 'url(#poco)')
        d.ret_c(cx, cy, 37.0, 25.0, '#121315', rx=2.0, stroke='#2f3237', sw=0.3)
        d.ret_c(cx, cy - 1.0, 30.0, 18.0, '#2a2c31', rx=6.0)
        d.circ(cx, cy, 8.5, '#2d2f34', stroke='#3d4046', sw=0.2)
        for k in range(-5, 6):
            d.linha(cx + k * 1.6, cy - 12.0, cx + k * 1.6, cy - 10.6, '#4a4e55', 0.15)
        for sy in (-1, 1):
            d.ret_c(cx, cy + sy * 21.0, 0.7, 3.4, '#d02a24')
    # os trims (dois verticais no meio, dois horizontais embaixo) e a alca do pescoco cromada
    trim(d, 68.8, 106.5, True)
    trim(d, 105.0, 106.5, True)
    trim(d, 36.0, 134.0, False)
    trim(d, 141.0, 136.0, False)
    d.ret(83.5, 95.0, 7.0, 22.0, 'url(#cromo)', rx=1.0, stroke='#5d646b', sw=0.15)
    d.ret(85.0, 97.0, 4.0, 4.0, '#1d1e21', rx=0.6)
    d.ret(85.0, 103.0, 4.0, 4.0, '#1d1e21', rx=0.6)
    # o painel de baixo (medido na foto): a placa grafite-azulada, o filete azul,
    # a faixa de cima com o modelo e a de baixo com os textos
    painel = [(28.0, 135.6), (146.0, 135.6), (150.6, 139.4), (150.6, 182.4), (142.6, 191.4), (31.4, 191.4),
              (23.4, 184.0), (23.4, 139.6)]
    poligono(d, [(x + 0.3, y + 0.4) for x, y in painel], '#000000', op=0.35)
    poligono(d, painel, '#3e4552', stroke='#1b1e24', sw=0.35)
    poligono(d, [(x * 0.975 + 87 * 0.025, y * 0.975 + 163.5 * 0.025) for x, y in painel], None, stroke='#3149c4',
             sw=0.45)
    poligono(d, painel, 'url(#luz)')
    poligono(d, [(47.0, 138.2), (128.0, 138.2), (130.4, 141.6), (128.0, 145.0), (43.6, 145.0)], '#3149c4',
             stroke='#1b2780', sw=0.15)
    for k in range(6):
        xa = 74.0 + k * 2.2
        poligono(d, [(xa, 138.4), (xa + 1.0, 138.4), (xa + 0.1, 144.8), (xa - 0.9, 144.8)], '#e6e9f5', op=0.85)
    poligono(d, [(38.0, 183.6), (146.0, 183.6), (142.0, 189.2), (41.6, 189.2)], '#3149c4', stroke='#1b2780', sw=0.15)
    x0, y0 = RADIO['tela'][0] - RADIO['tela_wh'][0] / 2, RADIO['tela'][1] - RADIO['tela_wh'][1] / 2
    d.ret(x0 - 1.6, y0 - 1.6, RADIO['tela_wh'][0] + 3.2, RADIO['tela_wh'][1] + 3.2, '#2a2d33', rx=1.0)
    d.ret(x0, y0, RADIO['tela_wh'][0], RADIO['tela_wh'][1], 'url(#lcd)', rx=0.4)
    d.ret(x0, y0, RADIO['tela_wh'][0], RADIO['tela_wh'][1], 'url(#luz)', rx=0.4)
    # os botoes e os rotulos do painel
    for (x, y), t in (((33.0, 148.5), 'UP'), ((33.0, 158.5), 'DOWN')):
        botao_trapezio(d, x, y)
        d.texto(x + 9.5, y, t, 1.1, S, alinhar='center', peso='bold')
    for (x, y), t in (((140.6, 148.8), 'OK'), ((140.4, 158.6), 'CANCEL')):
        botao_trapezio(d, x, y)
        d.texto(x - 10.5, y, t, 1.1, S, alinhar='center', peso='bold')
    # o POWER (a chave deslizante prateada) e o BIND (o botao redondo)
    px, py = RADIO['liga']
    d.texto(px, py - 11.6, 'POWER', 1.1, S, alinhar='center', peso='bold')
    d.ret_c(px + 0.2, py + 0.3, 9.6, 18.8, '#000000', rx=0.8, op=0.4)
    d.ret_c(px, py, 9.6, 18.8, '#1b1c1f', rx=0.8, stroke='#0b0b0c', sw=0.15)
    d.ret_c(px, py - 2.0, 8.2, 13.0, 'url(#cromo)', rx=0.5, stroke='#5d646b', sw=0.12)
    for k in range(7):
        d.linha(px - 3.2, py + 0.5 + k * 0.95, px + 3.2, py + 0.5 + k * 0.95, '#4f555d', 0.3)
    poligono(d, [(px - 1.2, py - 4.0), (px + 1.2, py - 4.0), (px, py - 5.8)], '#4f555d')
    d.circ(35.8, 177.2, 7.4, '#3149c4', stroke='#1b2780', sw=0.2)
    d.circ(35.8, 177.2, 6.6, '#3e4552')
    d.circ(35.8 + 0.2, 177.2 + 0.3, 5.6, '#000000', op=0.4)
    d.circ(35.8, 177.2, 5.6, '#1c1d20', stroke='#3a3d43', sw=0.2)
    d.circ(35.8, 177.2, 5.6, 'url(#cupula)')
    d.texto(53.0, 186.4, 'BIND RANGE TEST', 1.3, S, alinhar='center', peso='bold')
    d.texto(108.0, 186.4, 'DIGITAL PROPORTIONAL RADIO CONTROL SYSTEM', 1.05, S, alinhar='center', peso='bold')
    d.texto(113.0, 141.6, 'FS-i6', 2.4, '#f2f4f7', alinhar='center', peso='bold')
    gravar('radio-controle', d, L, H)
    guardar('radio-controle', L, H, [])


# =====================================================================
#  FS-iA6B (receptor-rc), 47 x 26,2 mm
# =====================================================================
RX = dict(L=62.0, H=29.0, x0=12.5, y0=1.4, comp=47.0, larg=26.2)


def receptor():
    L, H = E.caixa(RX['L'], RX['H'])
    d = E.novo()
    x0, y0, c, w = RX['x0'], RX['y0'], RX['comp'], RX['larg']
    xb = x0 + c - 12.0                                    # o bloco do conector (12 mm, mais alto)
    # as duas antenas (o coaxial cinza), saindo da ponta de tras
    for ya, yb in ((y0 + 6.5, 3.0), (y0 + 11.0, 9.5)):
        fio(d, [(x0 + 0.5, ya), (x0 - 3.5, ya - 0.5), (x0 - 8.0, yb), (1.0, yb - 1.5)], '#8e949b', 1.1)
    d.ret(x0 + 0.3, y0 + 0.4, c, w, '#000000', rx=1.5, op=0.35)
    d.ret(x0, y0, xb - x0 + 1.0, w, 'url(#carcaca)', rx=1.5, stroke='#0b0b0c', sw=0.18)
    d.ret(x0 + 0.8, y0 + 0.8, xb - x0 - 1.0, w - 1.6, None, rx=1.0, stroke='#4a4d53', sw=0.15)
    # a etiqueta: o verde, o preto e o texto
    lx0, ly0 = x0 + 2.0, y0 + 2.2
    d.ret(lx0, ly0, xb - lx0 - 1.2, w - 4.4, '#18191b', rx=0.6)
    d.ret(lx0, ly0 + 2.8, xb - lx0 - 1.2, 7.2, '#2f9a45', rx=0.3)
    d.texto(lx0 + 1.0, ly0 + 4.6, '2.4GHz 6 CHANNEL RECEIVER', 1.05, '#f2f4f2', alinhar='center-left', peso='bold')
    d.texto(xb - 2.0, ly0 + 7.6, '4.0-8.4V/DC', 1.1, '#f2f4f2', alinhar='center-right', peso='bold')
    d.texto(xb - 2.0, ly0 + 5.9, 'AFHDS 2A', 0.8, '#ff6a3a', alinhar='center-right', peso='bold')
    d.texto((lx0 + xb) / 2, ly0 + 14.2, 'FS-iA6B', 3.4, '#f4f6f8', alinhar='center', peso='bold')
    d.ret(lx0 + 2.0, ly0 + 17.0, 17.0, 3.2, '#eef0ee', rx=0.4)
    d.texto(lx0 + 10.5, ly0 + 18.6, 'NO:H5158572', 1.05, '#2a2b2e', alinhar='center', peso='normal')
    # o LED e o furo do UPDATE (o botao do bind la dentro)
    d.texto(lx0 + 1.0, ly0 + 1.4, 'LED', 0.8, '#d9dcdf', alinhar='center-left', peso='bold')
    d.circ(lx0 + 5.2, ly0 + 1.4, 0.75, '#3a0d0b', stroke='#0b0b0c', sw=0.1)
    d.circ(lx0 + 10.5, ly0 + 1.4, 0.75, '#050506', stroke='#3a3d43', sw=0.1)
    d.texto(lx0 + 11.8, ly0 + 1.4, 'UPDATE', 0.8, '#d9dcdf', alinhar='center-left', peso='bold')
    # o bloco do conector: a tampa, a lista das portas e os pinos na ponta
    d.ret(xb, y0 - 0.3, x0 + c - xb, w + 0.6, '#1f2024', rx=1.4, stroke='#0b0b0c', sw=0.18)
    d.ret(xb, y0 - 0.3, x0 + c - xb, w + 0.6, 'url(#luz)', rx=1.4)
    ys = [y0 + 1.67 + k * PASSO for k in range(10)]
    nomes = ['IBUS', 'VCC', 'GND', 'CH6', 'CH5', 'CH4', 'CH3', 'CH2', 'CH1', 'PPM']
    rotulos = ['i-BUS', 'B/VCC +', 'B/VCC -', 'CH6', 'CH5', 'CH4', 'CH3', 'CH2', 'CH1', 'PPM']
    for y, t in zip(ys, rotulos):
        d.ret_c(xb + 4.6, y, 6.6, 1.9, '#eef0ee', rx=0.25)
        d.texto(xb + 4.6, y, t, 0.85, '#1b1c1f', alinhar='center', peso='bold')
    xp = x0 + c + 1.6
    for y in ys:
        d.ret(x0 + c - 2.6, y - 0.9, 2.4, 1.8, '#0c0d0f', rx=0.2)
        d.ret(x0 + c - 1.2, y - 0.32, 3.4, 0.64, 'url(#ouro)', rx=0.08)
        d.ret_c(xp, y, 0.9, 0.9, '#7a5c18')
        d.ret_c(xp, y, 0.64, 0.64, 'url(#ouro)')
    gravar('receptor-rc', d, L, H)
    guardar('receptor-rc', L, H, [(n, xp, y) for n, y in zip(nomes, ys)])
    print(f'  luz do receptor: ({(lx0 + 5.2) * A:.2f}, {(ly0 + 1.4) * A:.2f}) px')


# =====================================================================
#  ESC (o simples, no termo-retratil)
# =====================================================================
CORES_ESC = {
    'azul': ('#1f5fb4', '#123e7a'), 'preto': ('#26282c', '#0e0f11'), 'vermelho': ('#c0392b', '#7b2219'),
    'verde': ('#1e8449', '#114d2a'), 'roxo': ('#6c3483', '#40204e'), 'laranja': ('#d35400', '#7e3200'),
    'rosa': ('#d6337a', '#831f4b'),
}
# a corrente -> (comprimento, largura) do corpo (a linha Skywalker), as celulas e o BEC
ESC_MEDIDAS = {12: (38.0, 18.0, '2-3S', '1A'), 20: (42.0, 25.0, '2-3S', '2A'), 30: (52.0, 26.0, '2-3S', '2A'),
               40: (65.0, 25.0, '2-3S', '3A'), 60: (77.0, 35.0, '2-6S', '5A')}
ESC_L, ESC_H = 100.0, 40.0


def esc_desenho(amperes, cor):
    L, H = E.caixa(ESC_L, ESC_H)
    d = E.novo()
    comp, larg, celulas, bec = ESC_MEDIDAS[amperes]
    corpo, borda = CORES_ESC[cor]
    cx, cy = 50.0, 20.0
    xa, xb = cx - comp / 2, cx + comp / 2
    ys_m = [cy - 5.08, cy, cy + 5.08]
    ys_b = [cy - 12.7, cy - 7.62]
    ys_s = [cy + 2.54, cy + 5.08, cy + 7.62]
    # os fios do motor (preto) com os conectores bala femeas
    for y in ys_m:
        fio(d, [(xa + 1.0, cy + (y - cy) * 0.35), (xa - 4.0, cy + (y - cy) * 0.6), (9.0, y), (6.0, y)], '#1d1e21', 1.6)
        d.ret(1.0, y - 1.9, 6.4, 3.8, '#1b1c1f', rx=0.6)                        # o termo-retratil da bala
        d.ret(1.6, y - 1.6, 5.2, 3.2, 'url(#ouro)', rx=0.8)
        d.circ(1.6, y, 1.2, '#4a3a12')
    # a bateria (vermelho e preto) e o cabo de servo (laranja, vermelho e marrom)
    for y, c2 in zip(ys_b, ('#d23b33', '#1d1e21')):
        fio(d, [(xb - 1.0, cy - 7.0 + (y - ys_b[0]) * 0.5), (xb + 4.0, y), (ESC_L - 7.0, y)], c2, 1.6)
        d.ret(ESC_L - 7.4, y - 1.9, 6.4, 3.8, '#1b1c1f', rx=0.6)
        d.ret(ESC_L - 6.8, y - 1.6, 5.2, 3.2, 'url(#ouro)', rx=0.8)
    for y, c2 in zip(ys_s, ('#f08a1c', '#d23b33', '#7a4a26')):
        fio(d, [(xb - 1.0, cy + 4.0 + (y - ys_s[0]) * 0.4), (xb + 6.0, y), (ESC_L - 15.0, y)], c2, 0.75)
    # o conector do servo (o Futaba/JR preto de 3 vias)
    E.sombra(d, ESC_L - 15.0, ys_s[0] - 1.6, 14.0, 8.3, rx=0.4)
    d.ret(ESC_L - 15.0, ys_s[0] - 1.6, 14.0, 8.3, 'url(#plastico)', rx=0.4, stroke='#0a0a0b', sw=0.1)
    for y in ys_s:
        d.ret_c(ESC_L - 7.0, y, 4.0, 1.2, '#0b0b0c', rx=0.2)
        d.ret_c(ESC_L - 1.6, y, 0.9, 0.9, '#7a5c18')
        d.ret_c(ESC_L - 1.6, y, 0.6, 0.6, 'url(#ouro)')
    # o corpo: o termo-retratil translucido com o relevo da placa e dos capacitores
    E.sombra(d, xa, cy - larg / 2, comp, larg, rx=3.0, op=0.4)
    d.ret(xa, cy - larg / 2, comp, larg, corpo, rx=3.0, stroke=borda, sw=0.25)
    d.ret(xa + 2.0, cy - larg / 2 + 1.6, comp - 4.0, larg - 3.2, None, rx=2.0, stroke=borda, sw=0.35)
    for k in range(int(comp // 9)):
        x = xa + 5.0 + k * 9.0
        d.ret(x, cy - larg / 2 + 2.6, 6.0, larg * 0.32, borda, rx=1.0, op=0.45)      # os MOSFETs por baixo
    d.ret(xa, cy - larg / 2, comp, larg, 'url(#retratil)', rx=3.0)
    # a etiqueta (sem a marca): a corrente, o BEC e as celulas
    ew, eh = min(30.0, comp - 10.0), min(13.0, larg - 6.0)
    d.ret_c(cx, cy, ew, eh, '#f2f2ee', rx=0.8, stroke='#c9c9c2', sw=0.15)
    d.texto(cx, cy - eh * 0.18, f'{amperes}A', eh * 0.32, '#1b1c1f', alinhar='center', peso='bold')
    d.texto(cx, cy + eh * 0.24, f'BEC 5V/{bec}  {celulas}', eh * 0.12, '#4a4d53', alinhar='center', peso='bold')
    pinos = [(n, 1.6, y) for n, y in zip('ABC', ys_m)]
    pinos += [(n, ESC_L - 1.6, y) for n, y in zip(['BAT+', 'BAT-'], ys_b)]
    pinos += [(n, ESC_L - 1.6, y) for n, y in zip(['SINAL', '5V', 'GND'], ys_s)]
    return d, L, H, pinos


def escs():
    for amperes in ESC_MEDIDAS:
        for cor in CORES_ESC:
            d, L, H, _ = esc_desenho(amperes, cor)
            gravar(f'esc-{amperes}a-{cor}', d, L, H)
    d, L, H, pinos = esc_desenho(30, 'azul')
    gravar('esc', d, L, H)
    guardar('esc', L, H, pinos)


# =====================================================================
#  ESC 4-em-1 (41,6 x 40,5 mm, furos de 30,5 mm)
# =====================================================================
MASCARA_4EM1 = {
    'preto': ('#1b1d21', '#0a0b0d', '#2a2e35'), 'azul': ('#1d55a8', '#123a78', '#2d6cc6'),
    'vermelho': ('#b0352a', '#6c1d16', '#cc5242'), 'verde': ('#1d7a3d', '#114c25', '#2c9050'),
    'roxo': ('#5a2a8a', '#3a1a5a', '#6e3aa0'), 'laranja': ('#c25a12', '#7a380a', '#d8732a'),
    'rosa': ('#c23a76', '#7a2049', '#d8578f'),
}


def esc4em1_desenho(amperes, cor):
    L, H = E.caixa(41.6, 40.5)
    d = E.novo()
    mascara, borda, cobre = MASCARA_4EM1[cor]
    W, Hh = 41.6, 40.5
    cx, cy = W / 2, Hh / 2
    # a placa: os cantos chanfrados e os quatro furos de 30,5 mm
    ch = 3.0
    cont = [(ch, 0), (W - ch, 0), (W, ch), (W, Hh - ch), (W - ch, Hh), (ch, Hh), (0, Hh - ch), (0, ch)]
    poligono(d, [(x + 0.2, y + 0.3) for x, y in cont], '#000000', op=0.35)
    poligono(d, cont, mascara, stroke=borda, sw=0.2)
    # o cobre por baixo da mascara: os planos de cada fase e o barramento da bateria
    for sx in (-1, 1):
        for sy in (-1, 1):
            d.ret(cx + sx * 4.0 - (9.0 if sx < 0 else 0), cy + sy * 3.5 - (11.0 if sy < 0 else 0), 9.0, 11.0, cobre,
                  rx=0.6, op=0.55)
    d.ret(cx - 2.5, 4.0, 5.0, 32.5, cobre, rx=0.6, op=0.55)
    poligono(d, cont, 'url(#luz)')
    for sx in (-1, 1):
        for sy in (-1, 1):
            x, y = cx + sx * 15.25, cy + sy * 15.25
            d.circ(x, y, 2.6, OURO)
            d.circ(x, y, 2.0, '#1e1f22', stroke='#0b0b0c', sw=0.1)               # o ilho de borracha
            d.circ(x, y, 1.6, E.FURO)
    # as ilhas dos motores (tres por motor, nos lados) e as da bateria (embaixo)
    pinos = []
    for motor, (lado, ys) in enumerate(((-1, (6.0, 8.54, 11.08)), (1, (6.0, 8.54, 11.08)),
                                         (1, (28.92, 31.46, 34.0)), (-1, (28.92, 31.46, 34.0))), start=1):
        x = 1.5 if lado < 0 else W - 1.5
        for f, y in zip('ABC', ys):
            d.ret_c(x, y, 3.0, 2.0, ESTANHO, rx=0.4)
            d.ret_c(x, y, 3.0, 2.0, 'url(#luz)', rx=0.4)
            pinos.append((f'M{motor}{f}', x, y))
        xr = 4.6 if lado < 0 else W - 4.6
        d.texto(xr, ys[1], f'M{motor}', 0.9, SERIGRAFIA, ang=90, alinhar='center', peso='bold')
    for n, x in (('BAT+', 17.0), ('BAT-', 23.0)):
        d.ret_c(x, 38.5, 4.6, 3.0, ESTANHO, rx=0.5)
        d.ret_c(x, 38.5, 4.6, 3.0, 'url(#luz)', rx=0.5)
        d.texto(x, 35.6, '+' if n == 'BAT+' else '-', 1.4, SERIGRAFIA, alinhar='center', peso='bold')
        pinos.append((n, x, 38.5))
    # os 24 MOSFETs (DFN 3,3 x 3,3), seis por motor, perto das ilhas
    for motor, (sx, sy) in enumerate(((-1, -1), (1, -1), (1, 1), (-1, 1))):
        for i in range(3):
            for j in range(2):
                x = cx + sx * (7.4 + j * 4.1)
                y = cy + sy * (6.0 + i * 3.9)
                E.sombra(d, x - 1.65, y - 1.65, 3.3, 3.3, rx=0.2)
                d.ret_c(x + sx * 0.0, y, 3.3, 3.3, PRETO_CI, rx=0.2)
                d.ret_c(x, y, 3.3, 3.3, 'url(#luz)', rx=0.2)
                d.ret_c(x - sx * 1.95, y, 0.5, 2.6, ESTANHO, rx=0.05)
                d.circ(x - sx * 1.0, y - 1.0, 0.18, '#3a3c41')
        # o MCU do motor (QFN 4 x 4) e o capacitor do driver
        mx, my = cx + sx * 4.8, cy + sy * 2.6
        E.qfn(d, mx, my, 3.0, 6, (), 0.4)
        d.ret_c(mx, my, 3.0, 3.0, '#2a2c31', rx=0.12)
        d.circ(mx - 1.0, my - 1.0, 0.2, '#4a4d53')
        d.texto(mx, my + 0.2, 'BB21', 0.45, MARCA_CI, alinhar='center', peso='normal')
        E.passivo(d, cx + sx * 7.6, cy + sy * 2.6, '0603', 'C', vertical=True)
        E.passivo(d, cx + sx * 9.4, cy + sy * 2.6, '0603', 'R', vertical=True, codigo='103')
    # o capacitor de entrada (o eletrolitico SMD fica fora; aqui os ceramicos 1206)
    for y in (13.0, 27.5):
        E.passivo(d, cx, y, '1206', 'C', vertical=True)
    # o conector de sinal (SH 1,0 mm, 8 vias) em cima, com as ilhas dos sinais
    d.ret(12.6, 3.0, 16.4, 4.4, '#efece3', rx=0.4, stroke='#bdb7a8', sw=0.1)
    for k in range(8):
        d.ret_c(14.0 + k * 1.95, 4.3, 0.6, 1.6, '#3b352a')
    pinos_s = [('S1', 14.92), ('S2', 17.46), ('S3', 20.0), ('S4', 22.54), ('GND', 25.08)]
    for n, x in pinos_s:
        d.ret_c(x, 1.5, 1.6, 1.6, ESTANHO, rx=0.2)
        pinos.append((n, x, 1.5))
    d.texto(cx, 9.0, f'4in1 {amperes}A', 1.3, SERIGRAFIA, alinhar='center', peso='bold')
    d.texto(cx, 33.0, '3-6S', 1.1, SERIGRAFIA, alinhar='center', peso='bold')
    return d, L, H, pinos


def esc4em1():
    for amperes in (35, 45, 60):
        for cor in MASCARA_4EM1:
            d, L, H, _ = esc4em1_desenho(amperes, cor)
            gravar(f'esc-4em1-{amperes}a-{cor}', d, L, H)
    d, L, H, pinos = esc4em1_desenho(45, 'preto')
    gravar('esc-4em1', d, L, H)
    guardar('esc-4em1', L, H, pinos)


radio()
receptor()
escs()
esc4em1()

if __name__ == '__main__':
    cam = pa.BASE + 'app/assets/pecas.json'
    dados = json.load(io.open(cam, encoding='utf-8'))
    novos = {p['id'] for p in pecas}
    dados = [p for p in dados if p['id'] not in novos] + pecas
    dados.sort(key=lambda p: p['id'])
    io.open(cam, 'w', encoding='utf-8', newline='').write(json.dumps(dados, ensure_ascii=False, indent=1))
    print(f'catalogo: {len(dados)} pecas')
