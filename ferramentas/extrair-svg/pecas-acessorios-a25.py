"""python ferramentas/extrair-svg/pecas-acessorios-a25.py

As rodas e helices da Alpha 25, vistas de frente (o eixo apontando para quem
olha) e na escala real. Giram inteiras: o desenho todo e a parte "-movel" e o
"-corpo" fica vazio (encaixadas no eixo de um motor, giram com ele):

  - roda-tt     : a roda amarela de 65 mm dos carrinhos (o desenho com cota da
                  Yahboom: o pneu de 65 mm, o aro de 46 mm, o cubo de 11,3 mm
                  com o furo de duplo D 5,4/3,7; os cinco raios da foto);
  - helice-130  : a helice vermelha de duas pas e 75 mm dos kits de ventilador
                  do motor 130 (o contorno da pa tracado da foto de cima, o
                  cubo redondo com o furo de 2 mm);
  - helice-1045 : a 10 x 4,5" (254 mm) do A2212 (a pa tracada da foto de cima
                  da Wikimedia Commons, preta);
  - helice-5045 : a 5 x 4,5" de tres pas (127 mm) do drone de 5" (a pa tracada
                  da foto de cima; azul translucida, como as Gemfan).
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
mover, rodar = E.mover, E.rodar
PAS = json.load(io.open(os.path.join(AQUI, 'motores-cad', 'frentes-motores.json'), encoding='utf-8'))['pas']
pecas = []

DEFS_EXTRA = (
    '  <radialGradient id="cupula" cx="0.36" cy="0.3" r="0.78"><stop offset="0" stop-color="#ffffff" '
    'stop-opacity="0.6"/><stop offset="0.32" stop-color="#ffffff" stop-opacity="0.1"/><stop offset="1" '
    'stop-color="#000000" stop-opacity="0.32"/></radialGradient>\n'
    '  <radialGradient id="pneu" cx="0.5" cy="0.5" r="0.5"><stop offset="0.7" stop-color="#26272b"/>'
    '<stop offset="0.86" stop-color="#3a3c41"/><stop offset="1" stop-color="#141517"/></radialGradient>\n'
    '  <radialGradient id="aro" cx="0.42" cy="0.38" r="0.7"><stop offset="0" stop-color="#ffe866"/>'
    '<stop offset="0.7" stop-color="#f6cc22"/><stop offset="1" stop-color="#d9a514"/></radialGradient>\n')


def gravar(nome, d, L, H):
    s = E.svg(d, L, H).replace('</defs>', DEFS_EXTRA + '</defs>', 1)
    io.open(pa.BASE + f'app/assets/{nome}.svg', 'w', encoding='utf-8', newline='').write(s)


def tres(nome, L, H, desenho):
    d = E.novo()
    desenho(d)
    gravar(nome, d, L, H)
    gravar(nome + '-movel', d, L, H)
    vazio = E.novo()
    vazio.ret(0, 0, L, H, None)
    gravar(nome + '-corpo', vazio, L, H)
    pecas.append({'id': nome, 'largura': round(L * A), 'altura': round(H * A), 'pinos': [], 'variantes': [],
                  'metades': False})
    print(f'{nome}: {round(L * A)}x{round(H * A)} px')


def poligono(d, pts, fill, stroke=None, sw=0.0, op=None):
    d.poli(pts, fill, stroke=stroke, sw=sw, op=op)
    d.partes[-1] = d.partes[-1].replace('<path ', '<path stroke-linejoin="round" ', 1)


def suavizar(pts, n=2):
    """Media movel circular: tira o serrilhado do tracado da foto."""
    for _ in range(n):
        m = len(pts)
        pts = [((pts[i - 1][0] + 2 * pts[i][0] + pts[(i + 1) % m][0]) / 4,
                (pts[i - 1][1] + 2 * pts[i][1] + pts[(i + 1) % m][1]) / 4) for i in range(m)]
    return pts


def contorno_pa(chave, R, ponta_redonda=False):
    """O contorno tracado (fracao do raio) em mm, com a pa ao longo de +x."""
    c = PAS[chave]['contorno']
    meio = len(c) // 2
    l1, l2 = c[:meio], c[meio:][::-1]
    if ponta_redonda:
        # a foto corta a ponta na borda: fecha com um arco (a ponta das slow fly e redonda)
        corte = 0.955
        l1 = [p for p in l1 if p[0] <= corte]
        l2 = [p for p in l2 if p[0] <= corte]
        y1, y2 = l1[-1][1], l2[-1][1]
        rm, ym = (y2 - y1) / 2, (y1 + y2) / 2
        arco = [(corte + (1 - corte) * math.sin(math.radians(t)), ym - rm * math.cos(math.radians(t)))
                for t in range(10, 171, 20)]
        pts = l1 + arco + l2[::-1]
    else:
        pts = l1 + l2[::-1]
    # a raiz entra no cubo (a foto so mede a pa fora dele)
    pts = [(0.02, pts[0][1] * 0.85)] + pts + [(0.02, pts[-1][1] * 0.85)]
    pts = suavizar([(x * R, y * R) for x, y in pts], 2)
    return pts


# =====================================================================
#  a roda de 65 mm
# =====================================================================
def roda():
    L = H = 66.0
    c = 33.0

    def desenho(d):
        # o pneu: o flanco, a banda com os cravos em blocos (duas fileiras alternadas)
        d.circ(c + 0.3, c + 0.4, 32.5, '#000000', op=0.35)
        d.circ(c, c, 32.5, 'url(#pneu)', stroke='#0b0b0c', sw=0.3)
        for k in range(30):
            for fileira, (r0, r1, des) in enumerate(((29.6, 32.4, 0.0), (27.4, 29.2, 6.0))):
                a0 = math.radians(k * 12 + des)
                w = math.radians(4.2)
                pts = [(c + r0 * math.cos(a0 - w), c + r0 * math.sin(a0 - w)),
                       (c + r1 * math.cos(a0 - w * 0.9), c + r1 * math.sin(a0 - w * 0.9)),
                       (c + r1 * math.cos(a0 + w * 0.9), c + r1 * math.sin(a0 + w * 0.9)),
                       (c + r0 * math.cos(a0 + w), c + r0 * math.sin(a0 + w))]
                poligono(d, pts, '#33353a' if fileira == 0 else '#2c2e33', stroke='#141517', sw=0.25)
        d.circ(c, c, 27.0, None, '#121315', 0.5)
        d.circ(c, c, 25.0, '#1c1d20')
        # o aro amarelo (46 mm) e o lado de dentro dele (a borda de 42 mm)
        d.circ(c, c, 23.0, 'url(#aro)', stroke='#b8901a', sw=0.3)
        d.circ(c, c, 21.0, '#e0b11a', stroke='#c4960f', sw=0.2)
        # as cinco janelas entre os raios (o fundo e o lado de dentro do aro, mais escuro)
        for k in range(5):
            a0 = math.radians(-90 + 36 + k * 72)
            meia = math.radians(22)
            pts = []
            for t in range(-10, 11):
                a = a0 + meia * t / 10
                pts.append((c + 20.4 * math.cos(a), c + 20.4 * math.sin(a)))
            # os lados retos ate o vertice (os raios de 5 mm de cada lado)
            vx, vy = c + 9.0 * math.cos(a0), c + 9.0 * math.sin(a0)
            pts += [(vx + 1.6 * math.cos(a0 + 1.2), vy + 1.6 * math.sin(a0 + 1.2)),
                    (vx + 0.9 * math.cos(a0), vy + 0.9 * math.sin(a0)),
                    (vx + 1.6 * math.cos(a0 - 1.2), vy + 1.6 * math.sin(a0 - 1.2))]
            poligono(d, pts, '#8a6a0a', stroke='#a87e0e', sw=0.25)
            poligono(d, [(c + (x - c) * 0.97, c + (y - c) * 0.97) for x, y in pts], '#5a4508')
        # o cubo: o ressalto de 18 mm, o cilindro de 11,3 mm e o furo de duplo D
        d.circ(c + 0.15, c + 0.2, 9.0, '#000000', op=0.25)
        d.circ(c, c, 9.0, 'url(#aro)', stroke='#b8901a', sw=0.25)
        d.circ(c, c, 5.65, '#f2c42a', stroke='#b8901a', sw=0.25)
        d.circ(c, c, 5.65, 'url(#cupula)')
        r, h = 2.7, 1.85
        w = math.sqrt(r * r - h * h)
        d.caminho([('M', c - h, c - w), ('A', r, 0, 1, c + h, c - w), ('L', c + h, c + w),
                   ('A', r, 0, 1, c - h, c + w), ('Z',)], '#3a2c06', '#8a6a0a', 0.12)
        d.circ(c, c, 23.0, 'url(#luz)')

    tres('roda-tt', L, H, desenho)


# =====================================================================
#  as helices
# =====================================================================
def helice(nome, L, H, chave, R, n_pas, cor, borda, cubo, furo, op=None, ponta_redonda=False, marca=None):
    cx, cy = L / 2, H / 2
    pts = contorno_pa(chave, R, ponta_redonda)

    def desenho(d):
        for k in range(n_pas):
            ang = -90 + k * 360 / n_pas if n_pas == 3 else k * 180
            t = math.radians(ang)
            co, si = math.cos(t), math.sin(t)
            rot = [(cx + x * co - y * si, cy + x * si + y * co) for x, y in pts]
            poligono(d, [(x + 0.3, y + 0.4) for x, y in rot], '#000000', op=0.3)
            poligono(d, rot, cor, stroke=borda, sw=0.35, op=op)
            # o brilho da face (a curvatura da pa): o mesmo contorno encolhido para o bordo de ataque
            meio = [(cx + (x * 0.92) * co - (y * 0.55 - R * 0.012) * si, cy + (x * 0.92) * si + (y * 0.55 - R * 0.012) * co)
                    for x, y in pts]
            poligono(d, meio, '#ffffff', op=0.08)
        d.circ(cx + 0.2, cy + 0.3, cubo, '#000000', op=0.3)
        d.circ(cx, cy, cubo, cor, stroke=borda, sw=0.35, op=op)
        d.circ(cx, cy, cubo, 'url(#cupula)')
        d.circ(cx, cy, furo, '#0b0b0c', stroke=borda, sw=0.2)
        if marca:
            d.texto(cx + R * 0.32, cy + cubo * 0.1, marca, R * 0.045, '#8d949b', alinhar='center', peso='bold')

    tres(nome, L, H, desenho)


roda()
helice('helice-130', 76.0, 76.0, 'p130', 37.5, 2, '#e2342b', '#8a1a14', 4.4, 1.0)
helice('helice-1045', 256.0, 34.0, 'p1045', 127.0, 2, '#1f2126', '#060607', 7.0, 3.0, ponta_redonda=True,
       marca='10x4.5')
helice('helice-5045', 128.0, 128.0, 'p5045', 63.5, 3, '#2f8fe0', '#0d3f66', 6.0, 2.5, op=0.88)

if __name__ == '__main__':
    cam = pa.BASE + 'app/assets/pecas.json'
    dados = json.load(io.open(cam, encoding='utf-8'))
    novos = {p['id'] for p in pecas}
    dados = [p for p in dados if p['id'] not in novos] + pecas
    dados.sort(key=lambda p: p['id'])
    io.open(cam, 'w', encoding='utf-8', newline='').write(json.dumps(dados, ensure_ascii=False, indent=1))
    print(f'catalogo: {len(dados)} pecas')
