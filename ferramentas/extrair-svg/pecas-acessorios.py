"""python ferramentas/extrair-svg/pecas-acessorios.py

As rodas e helices da Alpha 23, em escala real e vistas de frente (o eixo
apontando para quem olha, como nos desenhos dos motores):

  - roda-tt      : a roda de 65 mm do motor amarelo (aro amarelo de seis
                   raios, pneu preto com cravos, cubo de duplo D);
  - helice-130   : a helice de tres pas (75 mm) do motor 130 dos kits;
  - helice-1045  : a helice 10x4,5" (254 mm) do brushless A2212.

Elas giram inteiras: o desenho todo e a parte "-movel", e o "-corpo" fica
vazio. Encaixadas no eixo de um motor (o editor encaixa ao soltar perto),
giram com ele.
"""
import io
import json
import math

BASE = 'C:/Users/seraf/OneDrive/Desktop/SimuladorESP32/'
PX = 96 / 25.4

pecas = []


def gravar_svg(nome, svg):
    io.open(BASE + f'app/assets/{nome}.svg', 'w', encoding='utf-8', newline='').write(svg)


def guardar(id_peca, larg_mm, alt_mm):
    pecas.append({
        'id': id_peca,
        'largura': round(larg_mm * PX),
        'altura': round(alt_mm * PX),
        'pinos': [],
        'variantes': [], 'metades': False,
    })
    print(f'{id_peca}: {round(larg_mm * PX)}x{round(alt_mm * PX)} px')


def cabeca(l, a):
    return ('<svg xmlns="http://www.w3.org/2000/svg" version="1.1"' + chr(10)
            + f'     width="{l}mm" height="{a}mm" viewBox="0 0 {l} {a}">' + chr(10))


def tres_partes(nome, l, a, desenho):
    # O corpo: so um retangulo transparente do tamanho da caixa (a peca gira inteira).
    vazio = f'  <rect x="0" y="0" width="{l}" height="{a}" fill="none"/>'
    gravar_svg(nome, cabeca(l, a) + desenho + chr(10) + '</svg>' + chr(10))
    gravar_svg(nome + '-corpo', cabeca(l, a) + vazio + chr(10) + '</svg>' + chr(10))
    gravar_svg(nome + '-movel', cabeca(l, a) + desenho + chr(10) + '</svg>' + chr(10))


def ponto(cx, cy, r, ang):
    return cx + r * math.cos(math.radians(ang)), cy + r * math.sin(math.radians(ang))


# =====================================================================
#  Roda de 65 mm (TT)
# =====================================================================
d = 66.0
c = d / 2
desenho = f'  <circle cx="{c}" cy="{c}" r="32.5" fill="#1d1e21" stroke="#0b0b0c" stroke-width="0.4"/>' + chr(10)
# os cravos do pneu
for k in range(36):
    x1, y1 = ponto(c, c, 32.3, k * 10)
    x2, y2 = ponto(c, c, 29.4, k * 10 + 3)
    desenho += f'  <path d="M{x1:.2f} {y1:.2f} L{x2:.2f} {y2:.2f}" stroke="#34363b" stroke-width="1.6"/>' + chr(10)
desenho += (f'  <circle cx="{c}" cy="{c}" r="27.0" fill="#2a2b2f"/>' + chr(10)
            + f'  <circle cx="{c}" cy="{c}" r="24.6" fill="#f2c230" stroke="#b8901a" stroke-width="0.4"/>' + chr(10)
            + f'  <circle cx="{c}" cy="{c}" r="20.6" fill="#2a2b2f"/>' + chr(10))
for k in range(6):
    ang = k * 60
    xa, ya = ponto(c, c, 6.0, ang - 9)
    xb, yb = ponto(c, c, 6.0, ang + 9)
    xc, yc = ponto(c, c, 21.0, ang + 6)
    xd, yd = ponto(c, c, 21.0, ang - 6)
    desenho += f'  <path d="M{xa:.2f} {ya:.2f} L{xd:.2f} {yd:.2f} L{xc:.2f} {yc:.2f} L{xb:.2f} {yb:.2f} Z" fill="#f2c230" stroke="#b8901a" stroke-width="0.3"/>' + chr(10)
# um raio marcado de branco: da para ver o giro devagar
xm, ym = ponto(c, c, 16.0, -90)
desenho += (f'  <circle cx="{xm:.2f}" cy="{ym:.2f}" r="1.3" fill="#ffffff"/>' + chr(10)
            + f'  <circle cx="{c}" cy="{c}" r="7.2" fill="#f2c230" stroke="#b8901a" stroke-width="0.4"/>' + chr(10)
            + f'  <path d="M{c - 1.85:.2f} {c - 1.97:.2f} A2.7 2.7 0 0 1 {c + 1.85:.2f} {c - 1.97:.2f} V{c + 1.97:.2f} A2.7 2.7 0 0 1 {c - 1.85:.2f} {c + 1.97:.2f} Z" fill="#f7f7f4" stroke="#a5a59e" stroke-width="0.25"/>')
tres_partes('roda-tt', d, d, desenho)
guardar('roda-tt', d, d)


# =====================================================================
#  Helice de tres pas (75 mm) do motor 130
# =====================================================================
d = 76.0
c = d / 2
desenho = ''
for k in range(3):
    ang = -90 + k * 120
    # uma pa: gota alongada do cubo ate 37 mm
    p0 = ponto(c, c, 4.0, ang - 25)
    p1 = ponto(c, c, 20.0, ang - 14)
    p2 = ponto(c, c, 36.5, ang - 4)
    p3 = ponto(c, c, 37.5, ang + 3)
    p4 = ponto(c, c, 20.0, ang + 13)
    p5 = ponto(c, c, 4.0, ang + 25)
    desenho += (f'  <path d="M{p0[0]:.2f} {p0[1]:.2f} Q{p1[0]:.2f} {p1[1]:.2f} {p2[0]:.2f} {p2[1]:.2f} '
                f'Q{p3[0]:.2f} {p3[1]:.2f} {p4[0]:.2f} {p4[1]:.2f} L{p5[0]:.2f} {p5[1]:.2f} Z" '
                f'fill="{"#e2473d" if k else "#ff8a3d"}" stroke="#8a1f19" stroke-width="0.35"/>' + chr(10))
desenho += (f'  <circle cx="{c}" cy="{c}" r="5.0" fill="#e2473d" stroke="#8a1f19" stroke-width="0.4"/>' + chr(10)
            + f'  <circle cx="{c}" cy="{c}" r="1.1" fill="#5a1414"/>')
tres_partes('helice-130', d, d, desenho)
guardar('helice-130', d, d)


# =====================================================================
#  Helice 1045 (254 mm) do brushless
# =====================================================================
l, a = 256.0, 34.0
cx, cy = l / 2, a / 2
desenho = ''
for lado in (-1, 1):
    # a pa: larga perto do cubo, afinando ate a ponta
    pts = [(cx + lado * 8, cy - 4.0), (cx + lado * 60, cy - 13.0), (cx + lado * 112, cy - 7.5),
           (cx + lado * 126.5, cy - 1.0), (cx + lado * 124, cy + 4.0), (cx + lado * 70, cy + 6.0),
           (cx + lado * 8, cy + 4.0)]
    d_pa = 'M' + ' L'.join(f'{x:.2f} {y:.2f}' for x, y in pts) + ' Z'
    desenho += f'  <path d="{d_pa}" fill="#23262c" stroke="#0b0b0c" stroke-width="0.5" stroke-linejoin="round"/>' + chr(10)
    desenho += f'  <path d="M{cx + lado * 30:.2f} {cy - 6.5:.2f} L{cx + lado * 100:.2f} {cy - 7.0:.2f}" stroke="#3a3e46" stroke-width="1.2"/>' + chr(10)
# a ponta de uma pa pintada (para ver o giro)
desenho += f'  <path d="M{cx + 112:.2f} {cy - 7.5:.2f} L{cx + 126.5:.2f} {cy - 1.0:.2f} L{cx + 124:.2f} {cy + 4.0:.2f} L{cx + 110:.2f} {cy + 5.0:.2f} Z" fill="#ff8a3d"/>' + chr(10)
desenho += (f'  <circle cx="{cx}" cy="{cy}" r="9.0" fill="#2c2f36" stroke="#0b0b0c" stroke-width="0.5"/>' + chr(10)
            + f'  <circle cx="{cx}" cy="{cy}" r="4.0" fill="#c9cdd3" stroke="#6d737c" stroke-width="0.4"/>' + chr(10)
            + f'  <text x="{cx}" y="{cy + 15.0:.2f}" fill="#9aa0a8" font-size="3.4px" text-anchor="middle" font-family="Verdana" font-weight="bold">1045</text>')
tres_partes('helice-1045', l, a, desenho)
guardar('helice-1045', l, a)


# =====================================================================
#  Helice 5045 de tres pas (127 mm, 5") -- a do drone de corrida
# =====================================================================
d = 128.0
c = d / 2
desenho = ''
for k in range(3):
    ang = -90 + k * 120
    # a pa de drone: larga no meio, ponta arredondada (o "bullnose" e outro)
    p0 = ponto(c, c, 6.5, ang - 30)
    p1 = ponto(c, c, 30.0, ang - 15)
    p2 = ponto(c, c, 58.0, ang - 6)
    p3 = ponto(c, c, 63.5, ang + 2)
    p4 = ponto(c, c, 58.0, ang + 9)
    p5 = ponto(c, c, 30.0, ang + 14)
    p6 = ponto(c, c, 6.5, ang + 30)
    desenho += (f'  <path d="M{p0[0]:.2f} {p0[1]:.2f} Q{p1[0]:.2f} {p1[1]:.2f} {p2[0]:.2f} {p2[1]:.2f} '
                f'Q{p3[0]:.2f} {p3[1]:.2f} {p4[0]:.2f} {p4[1]:.2f} Q{p5[0]:.2f} {p5[1]:.2f} {p6[0]:.2f} {p6[1]:.2f} Z" '
                f'fill="{"#2f9cf0" if k else "#ff8a3d"}" fill-opacity="0.88" stroke="#0d3f66" stroke-width="0.45"/>' + chr(10))
desenho += (f'  <circle cx="{c}" cy="{c}" r="7.5" fill="#2f9cf0" stroke="#0d3f66" stroke-width="0.5"/>' + chr(10)
            + f'  <circle cx="{c}" cy="{c}" r="2.6" fill="#c9cdd3" stroke="#6d737c" stroke-width="0.35"/>' + chr(10)
            + f'  <text x="{c}" y="{c + 12.5:.2f}" fill="#9aa0a8" font-size="3.0px" text-anchor="middle" font-family="Verdana" font-weight="bold">5045</text>')
tres_partes('helice-5045', d, d, desenho)
guardar('helice-5045', d, d)


cam = BASE + 'app/assets/pecas.json'
dd = json.load(io.open(cam, encoding='utf-8'))
novos = {p['id'] for p in pecas}
dd = [p for p in dd if p['id'] not in novos] + pecas
dd.sort(key=lambda p: p['id'])
io.open(cam, 'w', encoding='utf-8', newline='').write(json.dumps(dd, ensure_ascii=False, indent=1))
print(f'catalogo: {len(dd)} pecas')
