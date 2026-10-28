"""python ferramentas/extrair-svg/pecas-motores-a25.py

Os motores da Alpha 25, desenhados pelas fichas e pelos desenhos com cota de
cada um (a parte que gira em "-movel", o resto em "-corpo", no mesmo quadro):

  - stepper-motor   : o NEMA 17 (17HS) de frente: a tampa de aluminio de
                      42,3 mm com os cantos chanfrados, os furos M3 a 31 mm, o
                      ressalto de 22 mm e o eixo de 5 mm com a face plana; o
                      conector JST-PH do lado e o cabo de 4 fios ate o
                      conector Dupont (preto A+, verde A-, vermelho B+, azul B-);
  - biaxial-stepper : o VID28-05 (o motor de dois eixos coaxiais dos painel de
                      carro) pela planta do fabricante: o oval de 64 x 35 mm, os
                      rebaixos, os quatro parafusos, os oito pinos (7,76 mm entre
                      os do mesmo motor, 22,86 mm entre as fileiras) e o eixo a
                      9,5 mm do centro;
  - motor-28byj48   : o 28BYJ-48 de cima pelo desenho com cota: a lata de 28 mm,
                      as orelhas com os furos a 35 mm, o eixo de latao (5 mm com
                      as faces a 3 mm) a 8 mm do centro, a capa azul dos fios e
                      o conector XH de 5 vias (azul, rosa, amarelo, laranja,
                      vermelho);
  - motor-dc        : o motor de escovas de frente, um desenho por modelo: o
                      FA-130 e os RS-360/380/540/545/550 pelas fichas oficiais da
                      Mabuchi (a vista de frente tracada: os furos de ventilacao,
                      os roscados, o mancal), o RS-775 pelo desenho com cota. O
                      editor aumenta o desenho pelo diametro do modelo, entao
                      cada frente e desenhada com a lata em 20 mm;
  - motor-tt        : o motor amarelo de reducao 1:48 (o "TT") pela face larga,
                      pelo desenho com cota (70 x 22,44 mm): a caixa amarela com
                      os furos de 3 mm, o eixo duplo D de 5,4/3,7 mm, a
                      presilha do motor e os fios com as pontas de jumper;
  - motor-brushless : o outrunner de cima. O A2212 (o padrao) pelo desenho da
                      XXD: o sino dourado de 27,7 mm com os quatro furos de 7 mm,
                      a cruzeta de fixacao e os fios com os conectores bala de
                      3,5 mm; os outros modelos pelo desenho de cada familia (o
                      de corrida 22xx, o de multirrotor 2814/3508, os pequenos
                      1105/1404 e o inrunner 3650 de carrinho).
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
OURO, ESTANHO = E.OURO, E.ESTANHO
PASSO = 2.54
FRENTES = json.load(io.open(os.path.join(AQUI, 'motores-cad', 'frentes-motores.json'), encoding='utf-8'))
CATALOGO = {p['id']: p for p in json.load(io.open(pa.BASE + 'app/assets/pecas.json', encoding='utf-8'))}
pecas = []
eixos = []           # (peca, x mm, y mm, L mm, H mm) para o Animadas e o EixosDosMotores

DEFS_EXTRA = (
    '  <radialGradient id="lata-topo" cx="0.4" cy="0.35" r="0.75"><stop offset="0" stop-color="#f1f3f5"/>'
    '<stop offset="0.6" stop-color="#c3c8ce"/><stop offset="1" stop-color="#8a9097"/></radialGradient>\n'
    '  <radialGradient id="aluminio" cx="0.35" cy="0.3" r="0.9"><stop offset="0" stop-color="#eef0f2"/>'
    '<stop offset="0.55" stop-color="#cdd1d5"/><stop offset="1" stop-color="#9ca2a8"/></radialGradient>\n'
    '  <radialGradient id="zinco" cx="0.38" cy="0.32" r="0.8"><stop offset="0" stop-color="#eef0d2"/>'
    '<stop offset="0.55" stop-color="#bfc08e"/><stop offset="1" stop-color="#7d7f55"/></radialGradient>\n'
    '  <radialGradient id="dourado" cx="0.38" cy="0.32" r="0.85"><stop offset="0" stop-color="#ffe7a0"/>'
    '<stop offset="0.5" stop-color="#e6b04a"/><stop offset="1" stop-color="#a8701c"/></radialGradient>\n'
    '  <radialGradient id="latao" cx="0.38" cy="0.32" r="0.8"><stop offset="0" stop-color="#fbe9a8"/>'
    '<stop offset="0.55" stop-color="#d4ab52"/><stop offset="1" stop-color="#93702a"/></radialGradient>\n'
    '  <radialGradient id="aco" cx="0.38" cy="0.32" r="0.8"><stop offset="0" stop-color="#f7f8f9"/>'
    '<stop offset="0.55" stop-color="#b8bec5"/><stop offset="1" stop-color="#7a8189"/></radialGradient>\n'
    '  <radialGradient id="cobre-bob" cx="0.5" cy="0.5" r="0.6"><stop offset="0" stop-color="#d98a4a"/>'
    '<stop offset="1" stop-color="#7a3d16"/></radialGradient>\n'
    '  <radialGradient id="cupula" cx="0.36" cy="0.3" r="0.78"><stop offset="0" stop-color="#ffffff" '
    'stop-opacity="0.6"/><stop offset="0.32" stop-color="#ffffff" stop-opacity="0.1"/><stop offset="1" '
    'stop-color="#000000" stop-opacity="0.32"/></radialGradient>\n'
    '  <linearGradient id="lata-lado" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#8e959c"/>'
    '<stop offset="0.25" stop-color="#eef1f4"/><stop offset="0.6" stop-color="#c2c8ce"/>'
    '<stop offset="1" stop-color="#727980"/></linearGradient>\n'
    '  <linearGradient id="amarelo-tt" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#ffe066"/>'
    '<stop offset="0.5" stop-color="#f6c72c"/><stop offset="1" stop-color="#d9a514"/></linearGradient>\n')


def svg_de(d, L, H):
    return E.svg(d, L, H).replace('</defs>', DEFS_EXTRA + '</defs>', 1)


def gravar(nome, d, L, H):
    io.open(pa.BASE + f'app/assets/{nome}.svg', 'w', encoding='utf-8', newline='').write(svg_de(d, L, H))


def gravar_tres(nome, L, H, corpo, movel=None):
    """nome.svg (o inteiro), nome-corpo.svg e nome-movel.svg, no mesmo quadro."""
    dc = E.novo()
    corpo(dc)
    gravar(nome + '-corpo' if movel else nome, dc, L, H)
    if movel:
        dm = E.novo()
        movel(dm)
        gravar(nome + '-movel', dm, L, H)
        df = E.novo()
        corpo(df)
        movel(df)
        gravar(nome, df, L, H)


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


# =====================================================================
#  as pecas comuns: fios, pontas e conectores
# =====================================================================
def suave(pts, passos=10):
    """A curva Catmull-Rom pelos pontos (o fio dobrando sem quina)."""
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


def fio(d, pts, cor, larg=1.0, borda=None, brilho=0.35):
    """O fio encapado: a borda escura, a cor e o brilho de cima."""
    c = suave(pts)
    borda = borda or '#00000088'
    d.poli(c, None, stroke='#000000', sw=larg * 1.18, fechado=False, cap='round')
    d.partes[-1] = d.partes[-1].replace('<path ', '<path stroke-linejoin="round" stroke-opacity="0.55" ', 1)
    d.poli(c, None, stroke=cor, sw=larg, fechado=False, cap='round')
    d.partes[-1] = d.partes[-1].replace('<path ', '<path stroke-linejoin="round" ', 1)
    off = [(x - larg * 0.18, y - larg * 0.18) for x, y in c]
    d.poli(off, None, stroke='#ffffff', sw=larg * 0.28, fechado=False, cap='round')
    d.partes[-1] = d.partes[-1].replace('<path ', f'<path stroke-linejoin="round" stroke-opacity="{brilho}" ', 1)


def ponta_estanhada(d, x, y0, y1, larg=0.8):
    """A ponta desencapada e estanhada do fio (de y0 a y1, para baixo)."""
    d.ret(x - larg * 0.42, y0, larg * 0.84, y1 - y0, 'url(#metal-h)', rx=larg * 0.4)
    d.linha(x - larg * 0.2, y0 + 0.2, x - larg * 0.2, y1 - 0.2, '#ffffff', larg * 0.12, so=0.5)


def ponta_jumper(d, x, y0, comp=8.0):
    """A ponta de jumper macho: o corpo preto de 2,5 mm e o pino dourado."""
    E.sombra(d, x - 1.25, y0, 2.5, comp, rx=0.3)
    d.ret(x - 1.25, y0, 2.5, comp, 'url(#plastico)', rx=0.3, stroke='#0a0a0b', sw=0.08)
    d.ret(x - 1.25, y0 + 1.2, 2.5, 0.35, '#000000', op=0.35)
    d.ret(x - 0.32, y0 + comp, 0.64, 2.2, 'url(#ouro)', rx=0.1)
    return y0 + comp + 1.6


def conector_bala(d, x, y0, comp=7.5, diam=3.5):
    """O conector bala de 3,5 mm (o macho dourado, com a fenda da mola)."""
    E.sombra(d, x - diam / 2, y0, diam, comp, rx=diam / 2)
    d.ret(x - diam * 0.48, y0, diam * 0.96, 2.2, '#1b1c1f', rx=0.3)                  # o termo-retratil
    d.ret(x - diam / 2, y0 + 2.0, diam, comp - 2.0, 'url(#ouro)', rx=diam * 0.35)
    d.ret(x - diam / 2, y0 + 2.0, diam, comp - 2.0, 'url(#metal-h)', rx=diam * 0.35, op=0.25)
    d.linha(x, y0 + comp * 0.55, x, y0 + comp - 0.4, '#7a5a12', 0.25)                  # a fenda da mola
    d.ret(x - diam * 0.42, y0 + comp * 0.48, diam * 0.84, 0.35, '#8a6a20', rx=0.1)
    return y0 + comp - 0.6


def dupont_femea(d, xs, y0, comp=14.0):
    """O conector Dupont femea de n vias, deitado: as janelas da trava em cima
    e a boca de cada via na ponta (onde o pino entra)."""
    x0, x1 = min(xs) - 1.27, max(xs) + 1.27
    E.sombra(d, x0, y0, x1 - x0, comp, rx=0.3)
    d.ret(x0, y0, x1 - x0, comp, 'url(#plastico)', rx=0.3, stroke='#0a0a0b', sw=0.1)
    for x in xs:
        d.ret_c(x, y0 + comp * 0.42, 1.3, 2.4, '#0b0b0c', rx=0.15)                   # a janela da trava
        d.ret_c(x, y0 + comp * 0.42, 0.7, 1.6, '#a88a3a', rx=0.1, op=0.8)
        d.ret_c(x, y0 + comp - 1.3, 1.1, 1.1, '#050506', rx=0.12)                    # a boca
        d.ret_c(x, y0 + comp - 1.3, 0.5, 0.5, '#6a5520')
    for xa, xb in zip(xs, xs[1:]):
        d.linha((xa + xb) / 2, y0 + 0.4, (xa + xb) / 2, y0 + comp - 0.4, '#000000', 0.12, so=0.6)
    d.ret(x0, y0, x1 - x0, comp, 'url(#luz)', rx=0.3)
    return y0 + comp - 1.3


def xh_femea(d, xs, y0, comp=6.0, passo=2.5):
    """O conector XH (2,5 mm) femea branco, deitado, com a trava em cima."""
    x0, x1 = min(xs) - passo / 2 - 0.45, max(xs) + passo / 2 + 0.45
    E.sombra(d, x0, y0, x1 - x0, comp, rx=0.3)
    d.ret(x0, y0, x1 - x0, comp, '#f1eee6', rx=0.3, stroke='#bdb7a8', sw=0.1)
    d.ret(x0 + 0.5, y0 + 0.5, x1 - x0 - 1.0, 1.6, '#e2ddd0', rx=0.2)                  # a trava
    for x in xs:
        d.ret_c(x, y0 + comp - 1.25, 1.0, 1.0, '#3b352a', rx=0.1)
        d.ret_c(x, y0 + comp - 1.25, 0.5, 0.5, '#b08a3a')
    for xa, xb in zip(xs, xs[1:]):
        d.linha((xa + xb) / 2, y0 + 2.4, (xa + xb) / 2, y0 + comp - 0.3, '#c9c3b4', 0.15)
    d.ret(x0, y0, x1 - x0, comp, 'url(#luz)', rx=0.3)
    return y0 + comp - 1.25


def parafuso_phillips(d, x, y, r, cor='url(#aco)', giro=20):
    d.circ(x + 0.08, y + 0.1, r, '#000000', op=0.35)
    d.circ(x, y, r, cor, stroke='#5d646b', sw=0.08)
    d.entrar(mover(x, y) * rodar(giro))
    d.ret_c(0, 0, r * 1.3, r * 0.28, '#3e4349', rx=0.05)
    d.ret_c(0, 0, r * 0.28, r * 1.3, '#3e4349', rx=0.05)
    d.sair()


def poligono(d, pts, fill, stroke=None, sw=0.0, op=None):
    d.poli(pts, fill, stroke=stroke, sw=sw, op=op)
    d.partes[-1] = d.partes[-1].replace('<path ', '<path stroke-linejoin="round" ', 1)


def arco_anel(cx, cy, r0, r1, a0, a1):
    """O anel entre os raios r0 e r1, de a0 a a1 (graus), como caminho."""
    p = lambda r, a: (cx + r * math.cos(math.radians(a)), cy + r * math.sin(math.radians(a)))
    g = 1 if (a1 - a0) % 360 > 180 else 0
    return [('M', *p(r1, a0)), ('A', r1, g, 1, *p(r1, a1)), ('L', *p(r0, a1)), ('A', r0, g, 0, *p(r0, a0)), ('Z',)]


# =====================================================================
#  NEMA 17 (stepper-motor)
# =====================================================================
def nema17():
    L, H = E.caixa(44.3, 60.0)
    cx, cy, lado, ch = 22.15, 22.15, 42.3, 5.5
    s = lado / 2
    quadro = [(cx - s + ch, cy - s), (cx + s - ch, cy - s), (cx + s, cy - s + ch), (cx + s, cy + s - ch),
              (cx + s - ch, cy + s), (cx - s + ch, cy + s), (cx - s, cy + s - ch), (cx - s, cy - s + ch)]
    xs = [cx + (k - 1.5) * PASSO for k in range(4)]
    cores = ['#2e9e4a', '#202124', '#d23b33', '#2c63c4']          # A- verde, A+ preto, B+ vermelho, B- azul

    def corpo(d):
        # o cabo: do conector JST-PH do lado do motor ate o Dupont
        jx = [cx + (k - 1.5) * 2.0 for k in range(4)]
        for k in range(4):
            fio(d, [(jx[k], cy + s + 3.0), (jx[k], cy + s + 5.5), (xs[k], 49.0), (xs[k], 51.0)], cores[k], 1.05)
        # o conector JST-PH de 6 vias (o macho do motor e o plugue do cabo) por baixo da tampa
        E.sombra(d, cx - 7.0, cy + s - 2.0, 14.0, 6.0, rx=0.3)
        d.ret(cx - 7.0, cy + s - 2.0, 14.0, 4.6, '#efece3', rx=0.3, stroke='#bdb7a8', sw=0.1)
        d.ret(cx - 6.0, cy + s + 1.8, 12.0, 2.2, '#f6f3ec', rx=0.25, stroke='#c9c3b4', sw=0.08)
        for k in range(6):
            d.ret_c(cx + (k - 2.5) * 2.0, cy + s + 1.0, 0.9, 0.7, '#c9c3b4', rx=0.1)
        yp = dupont_femea(d, xs, 50.0, 9.5)
        assert abs(yp - 58.2) < 0.01, yp
        # a sombra e a tampa de aluminio
        poligono(d, [(x + 0.25, y + 0.35) for x, y in quadro], '#000000', op=0.35)
        poligono(d, quadro, 'url(#aluminio)', stroke='#7d848b', sw=0.18)
        poligono(d, [(cx + (x - cx) * 0.975, cy + (y - cy) * 0.975) for x, y in quadro], None, stroke='#ffffff',
                 sw=0.18, op=None)
        d.partes[-1] = d.partes[-1].replace('<path ', '<path stroke-opacity="0.55" ', 1)
        poligono(d, quadro, 'url(#luz)')
        # os furos M3 rosqueados a 31 mm
        for sx in (-1, 1):
            for sy in (-1, 1):
                x, y = cx + sx * 15.5, cy + sy * 15.5
                d.circ(x, y, 1.75, '#9aa1a8')
                d.circ(x, y, 1.5, '#2b2e32')
                d.circ(x, y, 1.5, None, '#5b6168', 0.18)
                d.circ(x - 0.35, y - 0.35, 0.55, '#45494f')
        # o ressalto de centragem (22 mm, 2 mm de altura) e a vedacao do rolamento
        d.circ(cx + 0.15, cy + 0.2, 11.0, '#000000', op=0.25)
        d.circ(cx, cy, 11.0, 'url(#aluminio)', stroke='#7d848b', sw=0.15)
        d.circ(cx, cy, 10.6, None, '#ffffff', 0.15)
        d.circ(cx, cy, 6.2, '#9ea5ac', stroke='#7a8188', sw=0.1)
        d.circ(cx, cy, 5.0, '#1c1e21')
        d.circ(cx, cy, 4.4, None, '#3c4046', 0.25)

    def movel(d):
        # o eixo de 5 mm com a face plana de 0,5 mm (a que mostra o giro)
        r = 2.5
        h = r - 0.5
        w = math.sqrt(r * r - h * h)
        d.circ(cx + 0.12, cy + 0.15, r, '#000000', op=0.35)
        d.caminho([('M', cx - w, cy - h), ('A', r, 1, 0, cx + w, cy - h), ('Z',)], 'url(#aco)', '#6b7279', 0.1)
        d.linha(cx - w + 0.15, cy - h, cx + w - 0.15, cy - h, '#ffffff', 0.12, so=0.7)
        d.circ(cx, cy, 0.8, None, '#9aa1a8', 0.12)

    gravar_tres('stepper-motor', L, H, corpo, movel)
    guardar('stepper-motor', L, H, [(n, x, 58.2) for n, x in zip(['A-', 'A+', 'B+', 'B-'], xs)], {
        'A-': ([], 'Bobina A, o fio verde (A-)'), 'A+': ([], 'Bobina A, o fio preto (A+)'),
        'B+': ([], 'Bobina B, o fio vermelho (B+)'), 'B-': ([], 'Bobina B, o fio azul (B-)')})
    eixos.append(('stepper-motor', cx, cy, L, H))


# =====================================================================
#  VID28-05 (biaxial-stepper)
# =====================================================================
def vid28():
    L, H = E.caixa(66.0, 37.0)
    cx, cy = 33.0, 18.5
    rebaixos = FRENTES['vid28']['rebaixos']

    def oval(dx=0.0, encolhe=0.0):
        a, b, reta = 16.6 - encolhe, 17.5 - encolhe, 15.4
        pts = []
        for k in range(0, 181, 5):
            t = math.radians(k - 90)
            pts.append((cx + dx + reta + a * math.cos(t), cy + b * math.sin(t)))
        for k in range(0, 181, 5):
            t = math.radians(k + 90)
            pts.append((cx + dx - reta + a * math.cos(t), cy + b * math.sin(t)))
        return pts

    def corpo(d):
        poligono(d, [(x + 0.2, y + 0.3) for x, y in oval()], '#000000', op=0.35)
        poligono(d, oval(), '#1d1e21', stroke='#0a0a0b', sw=0.15)
        poligono(d, oval(encolhe=0.9), None, stroke='#34363b', sw=0.25)
        # os rebaixos da planta (as regioes fechadas da vista de cima, juntas onde
        # as linhas de cota as partiam): o fundo mais escuro e a quina iluminada
        for r in rebaixos:
            pts = [(cx + x, cy + y) for x, y in r]
            poligono(d, pts, '#131416', stroke='#2c2e33', sw=0.14)
            poligono(d, [(x + 0.18, y + 0.22) for x, y in pts], None, stroke='#0a0a0b', sw=0.2)
        poligono(d, oval(), 'url(#luz)')
        # os furos de fixacao (a 50 mm) e os pinos de centragem
        for sx in (-1, 1):
            d.circ(cx + sx * 25.0, cy - 9.0, 1.75, '#1d1e21', stroke='#2c2e33', sw=0.14)
            d.circ(cx + sx * 25.0, cy - 9.0, 0.9, '#050506', stroke='#3a3d42', sw=0.12)
            for sy in (-1, 1):
                d.circ(cx + sx * 18.55, cy + sy * 9.1, 0.75, '#2c2e33', stroke='#46494f', sw=0.12)
                d.circ(cx + sx * 18.55, cy + sy * 9.1, 0.35, '#0b0b0c')
        d.circ(cx, cy - 15.1, 0.9, '#050506', stroke='#3a3d42', sw=0.12)
        d.circ(cx, cy + 12.0, 0.9, '#2c2e33', stroke='#46494f', sw=0.12)
        d.circ(cx, cy + 15.1, 0.9, '#050506', stroke='#3a3d42', sw=0.12)
        # os quatro parafusos da carcaca
        for sx in (-1, 1):
            for sy in (-1, 1):
                parafuso_phillips(d, cx + sx * 15.4, cy + sy * 13.4, 1.55, giro=25)
        # os oito pinos (de cima so aparece a ponta)
        for x, y in PINOS_VID:
            d.ret_c(cx + x + 0.08, cy + y + 0.1, 0.7, 0.7, '#000000', op=0.4)
            d.ret_c(cx + x, cy + y, 0.7, 0.7, 'url(#metal)', rx=0.08, stroke='#6b7279', sw=0.06)
        # os eixos coaxiais: a coroa serrilhada, o eixo de fora (POM) e o de dentro (aco)
        ex, ey = cx + 9.5, cy
        for k in range(24):
            a = math.radians(k * 15)
            d.circ(ex + 2.65 * math.cos(a), ey + 2.65 * math.sin(a), 0.32, '#2a2c30')
        d.circ(ex, ey, 2.6, '#26282c', stroke='#46494f', sw=0.12)
        d.circ(ex, ey, 1.55, '#f1efe8', stroke='#b9b4a6', sw=0.1)
        d.circ(ex, ey, 1.55, 'url(#cupula)')
        d.circ(ex, ey, 0.5, 'url(#aco)', stroke='#6b7279', sw=0.06)

    gravar_tres('biaxial-stepper', L, H, corpo)
    nomes = ['A1-', 'A1+', 'B1+', 'B1-', 'A2-', 'A2+', 'B2+', 'B2-']
    guardar('biaxial-stepper', L, H, [(n, cx + x, cy + y) for n, (x, y) in zip(nomes, PINOS_VID)])
    print(f'  EixoDoBiaxial = ({(cx + 9.5) * A:.2f}, {cy * A:.2f}) px')


# os pinos do VID28-05: o motor 1 a esquerda (A1-, A1+ em cima; B1+, B1- embaixo), o 2 a direita
PINOS_VID = [(-19.38, -11.43), (-11.62, -11.43), (-11.62, 11.43), (-19.38, 11.43),
             (19.38, -11.43), (11.62, -11.43), (11.62, 11.43), (19.38, 11.43)]


# =====================================================================
#  28BYJ-48 (motor-28byj48)
# =====================================================================
def byj48():
    L, H = E.caixa(44.0, 50.0)
    cx, cy = 22.0, 16.0
    ex, ey = cx, cy - 8.0
    xs = [cx + (k - 2) * 2.5 for k in range(5)]
    cores = ['#2c63c4', '#f08cb4', '#f2c91d', '#f07c1e', '#d23b33']

    def flange():
        """A chapa de cima: o circulo da lata e as duas orelhas (R3,5 nos furos)."""
        pts = []
        R, ro, xo = 14.0, 3.5, 17.5
        # a tangente da orelha a lata: do topo da orelha ate o circulo
        for k in range(-90, 91, 6):                                  # orelha direita (de cima para baixo)
            a = math.radians(k)
            pts.append((cx + xo + ro * math.cos(a), cy + ro * math.sin(a)))
        for k in range(int(math.degrees(math.asin(ro / R))) + 4, 180 - int(math.degrees(math.asin(ro / R))) - 3, 4):
            a = math.radians(k)
            pts.append((cx + R * math.cos(a), cy + R * math.sin(a)))
        for k in range(90, 271, 6):                                  # orelha esquerda
            a = math.radians(k)
            pts.append((cx - xo + ro * math.cos(a), cy + ro * math.sin(a)))
        for k in range(180 + int(math.degrees(math.asin(ro / R))) + 4, 360 - int(math.degrees(math.asin(ro / R))) - 3, 4):
            a = math.radians(k)
            pts.append((cx + R * math.cos(a), cy + R * math.sin(a)))
        return pts

    def corpo(d):
        # os fios: da capa azul ao conector XH
        for k in range(5):
            x0 = cx + (k - 2) * 1.05
            fio(d, [(x0, 33.0), (x0, 36.0), (xs[k], 40.5), (xs[k], 42.5)], cores[k], 0.95)
        yp = xh_femea(d, xs, 42.0, 6.2)
        assert abs(yp - 46.95) < 0.01, yp
        # a capa azul da saida dos fios (14,6 mm, ate 18 mm do centro)
        E.sombra(d, cx - 7.3, cy + 9.0, 14.6, 9.0, rx=0.4)
        d.ret(cx - 7.3, cy + 9.0, 14.6, 9.0, '#2f7fd6', rx=0.4, stroke='#1d5aa0', sw=0.12)
        d.ret(cx - 6.4, cy + 15.6, 12.8, 2.0, '#2a6fbf', rx=0.3)
        for k in range(5):
            d.ret_c(cx + (k - 2) * 1.05, cy + 17.2, 0.85, 1.1, cores[k], rx=0.3)
        d.ret(cx - 7.3, cy + 9.0, 14.6, 9.0, 'url(#luz)', rx=0.4)
        # a chapa com as orelhas (zincada) e a lata
        poligono(d, [(x + 0.15, y + 0.2) for x, y in flange()], '#000000', op=0.35)
        poligono(d, flange(), '#b9bdb4', stroke='#7d8279', sw=0.15)
        poligono(d, flange(), 'url(#luz)')
        for sx in (-1, 1):
            d.circ(cx + sx * 17.5, cy, 2.1, E.FURO)
            d.circ(cx + sx * 17.5, cy, 2.1, None, '#80857c', 0.15)
        d.circ(cx, cy, 13.4, None, '#cfd3ca', 0.25)
        # as dobras de cravacao e as marcas da chapa
        for a in (-38, -142, 38, 142):
            t = math.radians(a)
            d.entrar(mover(cx + 11.6 * math.cos(t), cy + 11.6 * math.sin(t)) * rodar(a + 90))
            d.ret_c(0, 0, 2.2, 0.9, '#9ea296', rx=0.15, stroke='#7d8279', sw=0.08)
            d.sair()
        for x, y in ((5.5, -3.5), (4.0, 2.0), (1.0, 6.5), (-2.5, 4.5)):
            d.circ(cx + x, cy + y, 0.45, '#9ea296', stroke='#80857c', sw=0.08)
        d.circ(cx, cy, 0.35, '#80857c')
        # o mancal do eixo (o ressalto de 9 mm)
        d.circ(ex + 0.12, ey + 0.15, 4.5, '#000000', op=0.3)
        d.circ(ex, ey, 4.5, 'url(#zinco)', stroke='#6f735f', sw=0.12)
        d.circ(ex, ey, 3.4, None, '#ffffff', 0.12)
        d.circ(ex, ey, 2.75, '#3a3a33')

    def movel(d):
        # o eixo de latao de 5 mm com as duas faces a 3 mm (o duplo D)
        r, h = 2.5, 1.5
        w = math.sqrt(r * r - h * h)
        d.circ(ex + 0.12, ey + 0.15, r, '#000000', op=0.35)
        d.caminho([('M', ex - h, ey - w), ('A', r, 0, 1, ex + h, ey - w), ('L', ex + h, ey + w),
                   ('A', r, 0, 1, ex - h, ey + w), ('Z',)], 'url(#latao)', '#8a6a26', 0.1)
        d.linha(ex - h + 0.12, ey - w + 0.2, ex - h + 0.12, ey + w - 0.2, '#fff4c8', 0.14, so=0.8)
        d.circ(ex, ey, 0.55, '#b48a3c')

    gravar_tres('motor-28byj48', L, H, corpo, movel)
    guardar('motor-28byj48', L, H, [(n, x, 46.95) for n, x in zip(['azul', 'rosa', 'amarelo', 'laranja', 'vermelho'], xs)])
    eixos.append(('motor-28byj48', ex, ey, L, H))


# =====================================================================
#  as engrenagens e os eixos que mostram o giro
# =====================================================================
def pinhao(d, cx, cy, dentes, modulo, furo, cor='url(#latao)', borda='#7d6020', parafuso=False):
    """O pinhao reto visto de frente: dentes trapezoidais no modulo dado."""
    re, rr = (dentes + 2) * modulo / 2, (dentes - 2.5) * modulo / 2
    pts = []
    for k in range(dentes):
        a0 = 2 * math.pi * k / dentes
        passo = 2 * math.pi / dentes
        for f, r in ((0.0, rr), (0.18, rr), (0.32, re), (0.58, re), (0.72, rr)):
            a = a0 + f * passo
            pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    poligono(d, [(x + 0.1, y + 0.12) for x, y in pts], '#000000', op=0.35)
    poligono(d, pts, cor, stroke=borda, sw=0.08)
    d.circ(cx, cy, rr * 0.82, None, '#ffffff', 0.1)
    d.circ(cx, cy, furo / 2, 'url(#aco)', stroke='#5d646b', sw=0.08)
    d.circ(cx - furo * 0.12, cy - furo * 0.12, furo * 0.12, '#ffffff', op=0.6)
    if parafuso:                                     # o parafuso sem cabeca do cubo (mostra o giro)
        d.circ(cx + (re + rr) / 2 * 0.0, cy - rr * 0.9, modulo * 0.9, '#2a2c30')


def eixo_d(d, cx, cy, diam, plano, cor='url(#aco)', borda='#5d646b'):
    """A ponta do eixo com a face plana (o "D"): plano = a profundidade do corte."""
    r = diam / 2
    d.circ(cx + 0.1, cy + 0.12, r, '#000000', op=0.35)
    if plano <= 0:                                   # o eixo redondo: o chanfro da ponta e o centro
        d.circ(cx, cy, r, cor, borda, 0.08)
        d.circ(cx, cy, r * 0.72, None, '#ffffff', 0.08)
        d.circ(cx, cy, r * 0.25, '#8a9199')
        return
    h = r - plano
    w = math.sqrt(r * r - h * h)
    d.caminho([('M', cx - w, cy - h), ('A', r, 1, 0, cx + w, cy - h), ('Z',)], cor, borda, 0.08)
    d.linha(cx - w + 0.1, cy - h, cx + w - 0.1, cy - h, '#ffffff', 0.1, so=0.7)
    d.circ(cx, cy, r * 0.32, None, '#9aa1a8', 0.08)


# =====================================================================
#  motor DC de escovas (motor-dc): a frente de cada modelo, a lata em 20 mm
# =====================================================================
# modelo -> (diametro real, ressalto, anel de dentro, eixo, furos roscados (x, y, r), furos lisos, pinhao)
MODELOS_DC = {
    '130': ('fa130', 20.1, 6.15, 4.0, 2.0, [], [(0.0, 4.9, 0.55)], (8, 0.5, 'latao')),
    'rs-360': ('rs_360sh', 27.7, 10.0, 8.0, 2.3, [(1.66, -7.82, 1.15), (-1.66, 7.82, 1.15)], [(10.25, 2.18, 1.13)],
               (10, 0.5, 'aco')),
    'rs-380': ('rs_380ph', 27.7, 10.0, 8.0, 2.3, [(-0.42, -7.99, 1.15), (0.42, 7.99, 1.15)], [(10.48, -0.68, 1.0)],
               (10, 0.5, 'aco')),
    'rs-540': ('rs_540sh', 35.8, 13.0, 10.2, 3.17, [(-0.65, -12.48, 1.35), (0.65, 12.48, 1.35)],
               [(14.0, -0.56, 0.88)], (13, 0.6, 'aco')),
    'rs-545': ('rs_545sh', 35.8, 13.0, 10.2, 3.17, [(-3.23, -12.07, 1.35), (3.23, 12.07, 1.35)],
               [(14.16, -3.8, 1.1)], (13, 0.6, 'aco')),
    'rs-550': ('rs_550', 35.8, 13.0, 10.2, 3.17, [(0.0, -12.48, 1.35), (0.0, 12.48, 1.35)], [(-14.15, 0.0, 1.05)],
               (13, 0.6, 'aco')),
    'rs-775': (None, 42.0, 17.5, 13.0, 5.0, [(0.0, -14.5, 1.7), (0.0, 14.5, 1.7)],
               [(-16.4, 0.0, 0.6), (16.4, 0.0, 0.6)], None),
}
MODELOS_DC['personalizado'] = MODELOS_DC['rs-540']


def motor_dc():
    L, H = E.caixa(28.0, 30.0)
    cx, cy = 14.0, 12.5
    xs = [cx - PASSO / 2, cx + PASSO / 2]

    def frente(d, modelo):
        ficha, D, ressalto, anel, eixo_mm, roscados, lisos, _ = MODELOS_DC[modelo]
        R = D / 2
        s = 20.0 / D
        # os fios saem de tras da lata, por baixo (vermelho +, preto -)
        for x, cor in zip(xs, ('#d23b33', '#25262a')):
            fio(d, [(x, cy + 5.0), (x, cy + 10.5), (x, 25.4)], cor, 0.9)
            ponta_estanhada(d, x, 25.3, 29.7, 0.9)
        d.entrar(mover(cx, cy) * T(s, 0, 0, s, 0, 0))
        if modelo == '130':
            # a tampa de tras (branca) e os terminais aparecem por cima da face plana
            hp = 7.55
            d.ret(-6.36, -hp - 1.4, 12.73, 2.2, '#efece4', rx=0.5, stroke='#bcb6a8', sw=0.1)
            for x in (-4.25, 4.25):
                d.ret_c(x, -hp - 1.1, 1.6, 1.0, 'url(#latao)', rx=0.15)
            a = math.degrees(math.asin(hp / R))
            pts = []
            for k in range(0, 41):
                t = math.radians(-a + 2 * a * k / 40)
                pts.append((R * math.cos(t), R * math.sin(t)))
            for k in range(0, 41):
                t = math.radians(180 - a + 2 * a * k / 40)
                pts.append((R * math.cos(t), R * math.sin(t)))
            poligono(d, [(x + 0.25, y + 0.3) for x, y in pts], '#000000', op=0.35)
            poligono(d, pts, 'url(#lata-topo)', stroke='#6e757c', sw=0.18)
            poligono(d, [(x * 0.95, y * 0.93) for x, y in pts], None, stroke='#ffffff', sw=0.15)
            d.partes[-1] = d.partes[-1].replace('<path ', '<path stroke-opacity="0.5" ', 1)
        else:
            d.circ(0.25, 0.3, R, '#000000', op=0.35)
            d.circ(0, 0, R, 'url(#lata-topo)', stroke='#6e757c', sw=0.2)
            d.circ(0, 0, R - 0.55, None, '#ffffff', 0.18)
            d.circ(0, 0, R - 0.95, None, '#9aa1a8', 0.12)
        # os furos de ventilacao (tracados da ficha) e os furos
        if ficha and modelo != '130':          # a frente do 130 e lisa (so o mancal e o furinho)
            for r in FRENTES[ficha]['regioes']:
                cxr, cyr = r['c']
                if r['area'] < 5 or math.hypot(cxr, cyr) < ressalto / 2 + 1.0 or math.hypot(cxr, cyr) > R - 0.8:
                    continue
                poligono(d, r['pts'], '#1d1714', stroke='#5d646b', sw=0.14)
                poligono(d, [(x * 0.97, y * 0.97) for x, y in r['pts']], None, stroke='#8a4a22', sw=0.35)
                d.partes[-1] = d.partes[-1].replace('<path ', '<path stroke-opacity="0.55" ', 1)
        if modelo == 'rs-775':
            for a in (45, 135, 225, 315):
                d.caminho(arco_anel(0, 0, 12.4, 17.0, a - 17, a + 17), '#1d1714', '#5d646b', 0.14)
                d.caminho(arco_anel(0, 0, 13.3, 13.6, a - 15, a + 15), '#6a4a30')
        for x, y, r in roscados:
            d.circ(x, y, r * 1.18, '#9aa1a8')
            d.circ(x, y, r, '#2b2e32')
            d.circ(x, y, r * 0.82, None, '#5b6168', 0.12)
        for x, y, r in lisos:
            d.circ(x, y, r, '#141517', stroke='#7a8188', sw=0.1)
        # o mancal: o ressalto, o anel e a bucha
        d.circ(0.12, 0.15, ressalto / 2, '#000000', op=0.25)
        d.circ(0, 0, ressalto / 2, 'url(#lata-topo)', stroke='#7a8188', sw=0.14)
        d.circ(0, 0, ressalto / 2 - 0.3, None, '#ffffff', 0.12)
        d.circ(0, 0, anel / 2, None, '#8d949b', 0.16)
        if modelo == 'rs-775':
            d.circ(0, 0, 4.0, '#3a3d42', stroke='#7a8188', sw=0.12)          # o rolamento
            d.circ(0, 0, 3.3, None, '#9aa1a8', 0.2)
        else:
            d.circ(0, 0, eixo_mm / 2 + 0.55, '#a68a5a', stroke='#6d5a36', sw=0.08)   # a bucha de bronze
        d.sair()

    def ponta(d, modelo):
        _, D, _, _, eixo_mm, _, _, pin = MODELOS_DC[modelo]
        s = 20.0 / D
        d.entrar(mover(cx, cy) * T(s, 0, 0, s, 0, 0))
        if pin:
            dentes, modulo, mat = pin
            pinhao(d, 0, 0, dentes, modulo, eixo_mm, 'url(#latao)' if mat == 'latao' else 'url(#aco)',
                   '#7d6020' if mat == 'latao' else '#5d646b', parafuso=modelo != '130')
        else:
            eixo_d(d, 0, 0, eixo_mm, 0.5)
        d.sair()

    for modelo in MODELOS_DC:
        dc = E.novo()
        frente(dc, modelo)
        gravar(f'motor-dc-{modelo}-corpo', dc, L, H)
        dm = E.novo()
        ponta(dm, modelo)
        gravar(f'motor-dc-{modelo}-movel', dm, L, H)
    gravar_tres('motor-dc', L, H, lambda d: frente(d, '130'), lambda d: ponta(d, '130'))
    guardar('motor-dc', L, H, [('+', xs[0], 28.8), ('-', xs[1], 28.8)])
    eixos.append(('motor-dc', cx, cy, L, H))


# =====================================================================
#  motor TT (o amarelo de reducao 1:48), pela face larga
# =====================================================================
def motor_tt():
    """O desenho com cota (70 x 22,44 mm), espelhado: a orelha a esquerda, o
    motor a direita. x0 e a ponta da orelha; o eixo a 16,05 mm dela."""
    L, H = E.caixa(78.5, 26.0)
    x0, cy = 1.0, 13.0
    ex = x0 + 16.05
    yv, yb = cy - 11.22, cy + 11.22
    ys = [cy - PASSO / 2, cy + PASSO / 2]

    def corpo(d):
        # os fios: dos terminais da tampa do motor para a direita
        for y, yt, cor in ((ys[0], cy - 4.25, '#d23b33'), (ys[1], cy + 4.25, '#25262a')):
            fio(d, [(x0 + 69.0, yt), (x0 + 71.5, yt), (x0 + 73.0, y), (x0 + 74.2, y)], cor, 0.9)
            d.entrar(mover(x0 + 74.2, y) * rodar(-90))
            ponta_estanhada(d, 0, 0, 3.0, 0.9)
            d.sair()
        # a tampa de tras do motor (a das escovas) e os terminais
        d.ret(x0 + 66.9, cy - 6.36, 3.1, 12.73, '#2a2b2f', rx=0.6, stroke='#111214', sw=0.12)
        d.ret(x0 + 66.9, cy - 6.36, 3.1, 12.73, 'url(#luz)', rx=0.6)
        for yt in (cy - 4.25, cy + 4.25):
            d.ret_c(x0 + 69.4, yt, 1.6, 1.4, 'url(#latao)', rx=0.15)
            d.circ(x0 + 69.6, yt, 0.55, ESTANHO)
        # a lata do motor (a face plana) e a presilha branca
        E.sombra(d, x0 + 53.0, cy - 10.05, 14.2, 20.1, rx=0.4)
        d.ret(x0 + 53.0, cy - 10.05, 14.2, 20.1, 'url(#lata-lado)', rx=0.4, stroke='#6e757c', sw=0.14)
        d.ret(x0 + 64.8, cy - 2.2, 1.4, 4.4, '#8e959c', rx=0.3)                   # o furo de ventilacao
        d.ret(x0 + 41.8, cy - 10.19, 11.4, 20.38, '#eeeee8', rx=0.6, stroke='#b9b9b1', sw=0.12)
        d.ret(x0 + 43.0, cy - 2.6, 6.6, 5.2, '#d9d9d1', rx=0.4, stroke='#b9b9b1', sw=0.1)     # a trava
        d.ret(x0 + 41.8, cy - 10.19, 11.4, 20.38, 'url(#luz)', rx=0.6)
        # a caixa amarela: a orelha, o corpo com os cantos e os furos
        d.ret(x0, cy - 2.5, 5.2, 5.0, 'url(#amarelo-tt)', rx=0.5, stroke='#b8901a', sw=0.12)
        d.circ(x0 + 1.9, cy, 1.5, E.FURO)
        d.circ(x0 + 1.9, cy, 1.5, None, '#b8901a', 0.12)
        E.sombra(d, x0 + 4.8, yv, 37.0, 22.44, rx=2.2)
        d.ret(x0 + 4.8, yv, 37.0, 22.44, 'url(#amarelo-tt)', rx=2.2, stroke='#b8901a', sw=0.16)
        d.ret(x0 + 5.6, yv + 0.8, 35.4, 20.84, None, rx=1.6, stroke='#fff0a0', sw=0.12)
        d.linha(x0 + 4.9, cy, x0 + 41.7, cy, '#d9a514', 0.12)                     # a emenda das metades
        for x, y, r in ((x0 + 38.55, cy - 8.8, 1.5), (x0 + 38.55, cy + 8.8, 1.5)):     # os furos de 3 mm
            d.circ(x, y, r + 0.45, '#e9b520')
            d.circ(x, y, r, E.FURO)
        for x, y in ((x0 + 35.8, yv + 1.5), (x0 + 35.8, yb - 1.5), (x0 + 22.4, yv + 1.1), (x0 + 22.4, yb - 1.1),
                     (x0 + 6.9, yv + 2.6), (x0 + 6.9, yb - 2.6)):
            d.circ(x, y, 0.65, '#c99a12', stroke='#a87e0e', sw=0.08)
        d.circ(x0 + 27.3, cy, 0.98, '#7a5e0a', stroke='#a87e0e', sw=0.1)            # o furo de 1,95 mm
        d.circ(ex + 0.15, cy + 0.2, 3.6, '#000000', op=0.25)                       # o ressalto do eixo
        d.circ(ex, cy, 3.6, 'url(#amarelo-tt)', stroke='#b8901a', sw=0.14)
        d.circ(ex, cy, 3.0, '#c99a12')

    def movel(d):
        # o eixo branco de duplo D (5,4 mm, 3,7 mm entre as faces)
        r, h = 2.7, 1.85
        w = math.sqrt(r * r - h * h)
        d.circ(ex + 0.12, cy + 0.15, r, '#000000', op=0.3)
        d.caminho([('M', ex - h, cy - w), ('A', r, 0, 1, ex + h, cy - w), ('L', ex + h, cy + w),
                   ('A', r, 0, 1, ex - h, cy + w), ('Z',)], '#f6f6f2', '#a5a59e', 0.12)
        d.caminho([('M', ex - h, cy - w), ('A', r, 0, 1, ex + h, cy - w), ('L', ex + h, cy + w),
                   ('A', r, 0, 1, ex - h, cy + w), ('Z',)], 'url(#cupula)')
        d.circ(ex, cy, 0.55, '#d8d8d2', stroke='#b0b0a8', sw=0.06)

    gravar_tres('motor-tt', L, H, corpo, movel)
    guardar('motor-tt', L, H, [('+', x0 + 76.2, ys[0]), ('-', x0 + 76.2, ys[1])])
    eixos.append(('motor-tt', ex, cy, L, H))


# =====================================================================
#  motor brushless (motor-brushless): o sino de cima, um desenho por familia
# =====================================================================
FAMILIAS_BL = {
    'a2212-1000': 'a2212', 'a2212-1400': 'a2212', '2216-880': 'multi', '2814-900': 'multi', '3508-700': 'multi',
    '2205-2300': 'corrida', '2207-1750': 'corrida', '2306-2450': 'corrida', '1105-5000': 'pequeno',
    '1404-3800': 'pequeno', '3650-3100': 'inrunner', 'personalizado': 'multi',
}


def motor_brushless():
    L, H = E.caixa(36.0, 44.0)
    cx, cy = 18.0, 18.0
    RB = (22 * 1.12 + 3.0) / 2                       # o sino do desenho base (o editor escala pelo modelo)
    xs = [cx - 5.08, cx, cx + 5.08]

    def fios(d, cores, ponta):
        for x, k, cor in zip(xs, (-1, 0, 1), cores):
            fio(d, [(cx + k * 1.5, cy + 8.0), (cx + k * 1.5, cy + RB + 1.0), (x, cy + RB + 4.5), (x, 35.6)], cor, 1.3)
            if ponta == 'bala':
                conector_bala(d, x, 35.2, 7.6, 3.4)
            else:
                ponta_estanhada(d, x, 35.4, 43.6, 1.3)

    def cruzeta(d):
        """A cruzeta de aluminio do A2212 (os quatro bracos, os furos das pontas)."""
        bracos = []
        for k in range(4):
            a = math.radians(45 + 90 * k)
            bracos.append(a)
        pts = []
        for a in bracos:
            for da, r in ((-0.30, 9.0), (-0.21, 17.6), (-0.12, 19.6), (0.0, 20.2), (0.12, 19.6), (0.21, 17.6),
                          (0.30, 9.0)):
                pts.append((cx + r * math.cos(a + da), cy + r * math.sin(a + da)))
            pts.append((cx + 7.2 * math.cos(a + math.pi / 4), cy + 7.2 * math.sin(a + math.pi / 4)))
        poligono(d, [(x + 0.2, y + 0.25) for x, y in pts], '#000000', op=0.35)
        poligono(d, pts, 'url(#aluminio)', stroke='#7d848b', sw=0.15)
        for a in bracos:
            x, y = cx + 17.5 * math.cos(a), cy + 17.5 * math.sin(a)
            d.circ(x, y, 1.6, E.FURO)
            d.circ(x, y, 1.6, None, '#8d949b', 0.15)

    def corpo(d, familia):
        if familia == 'a2212':
            fios(d, ('#25262a', '#f2c91d', '#d23b33'), 'bala')
            cruzeta(d)
            d.circ(cx, cy, RB + 0.15, '#c08a2a')                                  # a base dourada por baixo
        elif familia == 'inrunner':
            fios(d, ('#25262a', '#25262a', '#25262a'), 'bala')
        else:
            fios(d, ('#25262a', '#25262a', '#25262a'), 'bala' if familia == 'multi' else 'ponta')
            d.circ(cx + 0.2, cy + 0.25, RB + 0.3, '#000000', op=0.35)
            d.circ(cx, cy, RB + 0.3, '#3a3d42', stroke='#1b1c1f', sw=0.12)            # a base (o estator)

    def sino(d, familia):
        if familia == 'a2212':
            # o sino dourado: quatro furos de 7 mm no circulo de 16 mm, o cubo de 8 mm
            d.circ(cx + 0.25, cy + 0.3, RB, '#000000', op=0.35)
            d.circ(cx, cy, RB, 'url(#dourado)', stroke='#8a5a14', sw=0.18)
            d.circ(cx, cy, RB - 0.7, None, '#ffe9a8', 0.25)
            for k in range(4):
                a = math.radians(90 * k)
                x, y = cx + 8.0 * math.cos(a), cy + 8.0 * math.sin(a)
                d.circ(x, y, 3.5, '#1c140d', stroke='#7a4f12', sw=0.16)
                d.circ(x + 0.6 * math.cos(a), y + 0.6 * math.sin(a), 2.3, 'url(#cobre-bob)')
                d.circ(x + 0.6 * math.cos(a), y + 0.6 * math.sin(a), 1.1, '#3a2414', op=0.6)
                d.circ(x, y, 3.5, None, '#000000', 0.5, op=None)
            d.circ(cx + 0.12, cy + 0.15, 4.0, '#000000', op=0.3)
            d.circ(cx, cy, 4.0, 'url(#dourado)', stroke='#8a5a14', sw=0.14)
            d.circ(cx, cy, 3.3, None, '#ffe9a8', 0.15)
            eixo_d(d, cx, cy, 3.17, 0.0)
            d.ret_c(cx, cy - 2.9, 0.5, 1.2, '#7a4f12', rx=0.15)                       # o parafuso do cubo
        elif familia in ('corrida', 'multi'):
            # o sino de corrida (o 2207 da BrotherHobby: cinco janelas) ou o de
            # multirrotor (o MN3508 da T-Motor: seis aberturas entre bracos curvos),
            # com as janelas tracadas dos desenhos com cota e escaladas para o sino
            sino_d = FRENTES['sinos'][familia]
            k = RB / (sino_d['D'] / 2)
            d.circ(cx + 0.25, cy + 0.3, RB, '#000000', op=0.35)
            d.circ(cx, cy, RB, '#2b2d32' if familia == 'corrida' else '#23252a', stroke='#0f1012', sw=0.18)
            d.circ(cx, cy, RB - 0.45, None, '#a7adb4', 0.3)
            for jan in sino_d['janelas']:
                pts = [(cx + x * k, cy + y * k) for x, y in jan]
                poligono(d, pts, '#120d09', stroke='#9aa1a8', sw=0.16)
                # o estator visto pela janela: o enrolamento de cobre e a ponta do dente
                mx = sum(p[0] for p in pts) / len(pts)
                my = sum(p[1] for p in pts) / len(pts)
                rr = math.hypot(mx - cx, my - cy)
                ux, uy = (mx - cx) / rr, (my - cy) / rr
                d.circ(cx + ux * 9.4, cy + uy * 9.4, 1.55, 'url(#cobre-bob)')
                d.circ(cx + ux * 9.4, cy + uy * 9.4, 1.55, None, '#3a2414', 0.12)
                d.ret_c(cx + ux * 11.4, cy + uy * 11.4, 0.9, 0.9, '#6b7178', rx=0.2)
            if familia == 'corrida':
                # o cubo serrilhado de 9 mm e o eixo M5 com a rosca
                d.circ(cx, cy, 4.9 * k, '#3a3d42', stroke='#9aa1a8', sw=0.2)
                for i in range(-4, 5):
                    for j in range(-4, 5):
                        x, y = cx + i * 0.95 * k, cy + j * 0.95 * k
                        if 2.6 * k < math.hypot(x - cx, y - cy) < 4.4 * k:
                            d.ret_c(x, y, 0.62 * k, 0.62 * k, '#4a4e55', rx=0.05)
                d.circ(cx, cy, 2.5 * k, 'url(#aco)', stroke='#5d646b', sw=0.1)
                for r in (2.1, 1.65, 1.2):
                    d.circ(cx, cy, r * k, None, '#7a8189', 0.12)
                d.circ(cx, cy, 0.7 * k, '#7a8189')
                d.ret_c(cx, cy - 3.6 * k, 0.6, 0.9, '#d23b33', rx=0.15)
            else:
                # o cubo de 12 mm com os 4 x M3 (a 12 mm) e o eixo de 4 mm
                d.circ(cx, cy, 8.4 * k, '#2e3035', stroke='#b9bfc6', sw=0.2)
                for i in range(4):
                    a = math.radians(90 * i)
                    d.circ(cx + 6.0 * k * math.cos(a), cy + 6.0 * k * math.sin(a), 1.5 * k, '#0b0b0c',
                           stroke='#8d949b', sw=0.1)
                eixo_d(d, cx, cy, 4.0 * k, 0.0)
                d.ret_c(cx, cy - 7.4 * k, 0.6, 0.9, '#d23b33', rx=0.15)
        elif familia == 'pequeno':
            # o pequeno (o 1105 do desenho com cota): cinco bracos retos de 1,9 mm entre o
            # cubo de 8,6 mm e o aro, 4 x M2 no circulo de 5 mm, o eixo de 1,5 mm
            k = RB / 7.0
            d.circ(cx + 0.25, cy + 0.3, RB, '#000000', op=0.35)
            d.circ(cx, cy, RB, '#1f2024', stroke='#0f1012', sw=0.18)
            d.circ(cx, cy, RB - 0.45, None, '#8d949b', 0.3)
            r0, r1, meia = 4.5 * k, 6.35 * k, 0.95 * k
            for i in range(5):
                a0 = math.radians(90 + 72 * i)
                a1 = a0 + math.radians(72)
                pts = []
                for raio, ordem in ((r1, range(0, 11)), (r0, range(10, -1, -1))):
                    da = math.asin(meia / raio)
                    for t in ordem:
                        a = a0 + (a1 - a0) * t / 10
                        a += da if t == 0 else -da if t == 10 else 0
                        pts.append((cx + raio * math.cos(a), cy + raio * math.sin(a)))
                poligono(d, pts, '#120d09', stroke='#8d949b', sw=0.15)
                am = (a0 + a1) / 2
                d.circ(cx + 5.5 * k * math.cos(am), cy + 5.5 * k * math.sin(am), 0.65 * k, 'url(#cobre-bob)')
            d.circ(cx, cy, 4.3 * k, '#26282c', stroke='#8d949b', sw=0.2)
            for i in range(4):
                a = math.radians(90 * i)
                d.circ(cx + 2.5 * k * math.cos(a), cy + 2.5 * k * math.sin(a), 0.6 * k, '#0b0b0c',
                       stroke='#8d949b', sw=0.08)
            eixo_d(d, cx, cy, 1.5 * k, 0.0)
            d.ret_c(cx + 3.0 * k, cy - 3.0 * k, 0.6, 0.6, '#d23b33', rx=0.2)
        else:
            # o inrunner 3650: a frente da lata (36 mm no modelo; aqui 23 mm), 4 x M3
            # no circulo de 25 mm, o rolamento e o pinhao de 17 dentes no eixo de 3,175
            rl = RB * 36.0 / (36 * 1.12 + 3.0)
            k = rl / 18.0
            pinhao(d, cx, cy, 17, 0.6 * k, 3.175 * k, 'url(#aco)', '#5d646b', parafuso=True)

    def frente_inrunner(d):
        rl = RB * 36.0 / (36 * 1.12 + 3.0)
        k = rl / 18.0
        d.circ(cx + 0.25, cy + 0.3, rl, '#000000', op=0.35)
        d.circ(cx, cy, rl, '#2a5fae', stroke='#163a70', sw=0.18)                      # a tampa anodizada
        d.circ(cx, cy, rl - 0.6, None, '#6e9be0', 0.25)
        for a in (45, 135, 225, 315):
            t = math.radians(a)
            x, y = cx + 12.5 * k * math.cos(t), cy + 12.5 * k * math.sin(t)
            d.circ(x, y, 1.6 * k * 1.2, '#123064')
            d.circ(x, y, 1.25 * k * 1.2, '#0b0b0c')
        d.circ(cx, cy, 6.5 * k, '#2f66b8', stroke='#163a70', sw=0.14)
        d.circ(cx, cy, 4.2 * k, '#3a3d42', stroke='#8d949b', sw=0.14)                  # o rolamento

    for modelo, familia in FAMILIAS_BL.items():
        dc = E.novo()
        corpo(dc, familia)
        if familia == 'inrunner':
            frente_inrunner(dc)
        gravar(f'motor-brushless-{modelo}-corpo', dc, L, H)
        dm = E.novo()
        sino(dm, familia)
        gravar(f'motor-brushless-{modelo}-movel', dm, L, H)
    gravar_tres('motor-brushless', L, H, lambda d: corpo(d, 'a2212'), lambda d: sino(d, 'a2212'))
    guardar('motor-brushless', L, H, [(n, x, 42.7) for n, x in zip('ABC', xs)])
    eixos.append(('motor-brushless', cx, cy, L, H))


# =====================================================================
nema17()
vid28()
byj48()
motor_dc()
motor_tt()
motor_brushless()

if __name__ == '__main__':
    cam = pa.BASE + 'app/assets/pecas.json'
    dados = json.load(io.open(cam, encoding='utf-8'))
    novos = {p['id'] for p in pecas}
    dados = [p for p in dados if p['id'] not in novos] + pecas
    dados.sort(key=lambda p: p['id'])
    io.open(cam, 'w', encoding='utf-8', newline='').write(json.dumps(dados, ensure_ascii=False, indent=1))
    print('eixos (Animadas / EixosDosMotores):')
    for p, x, y, L, H in eixos:
        print(f'  {p}: ({x:.3f} mm, {y:.3f} mm) = ({x / L:.5f}, {y / H:.5f})')
