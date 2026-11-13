"""python ferramentas/extrair-svg/pecas-frames-a25.py

Os frames da Alpha 25: as placas cortadas a laser onde o projeto e montado --
o chassi do carrinho, o frame do drone, a base do braco robotico e o suporte
do servo. Cada uma sai do arquivo de corte do projeto aberto que a desenhou
(ferramentas/extrair-svg/frames-cad, origem e licenca no LICENSE de la), em
milimetros, e vai para app/assets/frames.json:

  - frame-2wd           : o chassi 2WD de acrilico dos kits de carrinho (o de
                          dasaki, tirado do STL do robo montado: a face de
                          cima da placa, no plano z 48,4);
  - frame-4wd           : o chassi 4WD com os recortes das rodas (sb43201,
                          a placa de cima, do STL);
  - frame-4wd-placa     : a placa de acrilico de 8 x 12" do carrinho 4WD de
                          Braden Boucher (o painel C do SVG de corte: o
                          retangulo de 203,2 x 304,8 mm e os furos dele);
  - frame-drone-5       : o Source Two v0.1 de 5" (220 mm entre motores): a
                          placa de baixo e os quatro bracos de 5", cada braco
                          posto pelos dois furos de parafuso que casam com os
                          da placa (as distancias batem em 12,000 mm);
  - frame-drone-6       : o mesmo com os bracos de 6" (245 mm);
  - frame-braco-mearm   : a placa de base do MeArm V3;
  - frame-braco-dearm   : a base do DE-Arm (compensado de 4 mm), sem o texto
                          gravado "DE Arm 2025";
  - frame-braco-servo   : a placa lateral do servo do ombro do DE-Arm (a do
                          "R", sem a letra gravada);
  - frame-suporte-sg90  : o suporte do micro servo SG90 do DE-Arm.

Nao entraram (sem arquivo de corte aberto e com cota): o F450/F550 (o manual
da DJI so tem o desenho de montagem), o Source One (sem licenca), o Source V
(bracos de pe) e os suportes pan-tilt e "U" de servo vendidos prontos.

O caminho de cada frame e o do SVG (contorno e furos, regra par-impar), em mm
com a origem no canto de cima a esquerda; furo redondo vai como circulo de
verdade (dois arcos). Precisa de ezdxf e shapely.
"""
import json
import math
import os
import re
import sys

import ezdxf
from ezdxf import disassemble
from ezdxf import path as epath
from shapely.affinity import affine_transform, translate
from shapely.geometry import LineString, Polygon, box
from shapely.ops import polygonize, unary_union

AQUI = os.path.dirname(os.path.abspath(__file__))
CAD = os.path.join(AQUI, 'frames-cad')
SAIDA = os.path.join(AQUI, '..', '..', 'app', 'assets', 'frames.json')


# ---------------------------------------------------------------------------
#  leitura
# ---------------------------------------------------------------------------

def linhas_do_dxf(nome, camada=None, tol=0.02):
    doc = ezdxf.readfile(os.path.join(CAD, nome))
    linhas = []
    for e in disassemble.recursive_decompose(doc.modelspace()):
        if e.dxftype() in ('TEXT', 'MTEXT', 'POINT', 'DIMENSION', 'HATCH', 'SOLID'):
            continue
        if camada and e.dxf.layer != camada:
            continue
        try:
            caminho = epath.make_path(e)
        except Exception:
            continue
        for sub in caminho.sub_paths():
            pts = [(round(v.x, 3), round(v.y, 3)) for v in sub.flattening(tol)]
            if len(pts) >= 2:
                linhas.append(LineString(pts))
    return linhas


def pecas_do_dxf(nome, camada=None):
    """As pecas fechadas do arquivo, maiores primeiro: (contorno, [furos])."""
    rede = unary_union([l for l in linhas_do_dxf(nome, camada) if l.length > 0])
    aneis = sorted((Polygon(p.exterior) for p in polygonize(rede)), key=lambda a: -a.area)
    aneis = [a for a in aneis if a.area > 0.05]
    ponto = [a.representative_point() for a in aneis]
    dentro = [[j for j in range(i) if aneis[j].area > a.area and aneis[j].contains(ponto[i])]
              for i, a in enumerate(aneis)]
    pecas = []
    for i, a in enumerate(aneis):
        if len(dentro[i]) % 2:
            continue
        furos = [aneis[j] for j in range(len(aneis))
                 if len(dentro[j]) == len(dentro[i]) + 1 and i in dentro[j]]
        pecas.append((a, furos))
    return pecas


def peca_de_tamanho(pecas, largura, altura):
    for contorno, furos in pecas:
        x0, y0, x1, y1 = contorno.bounds
        if abs(x1 - x0 - largura) < 0.05 and abs(y1 - y0 - altura) < 0.05:
            return contorno, furos
    raise SystemExit(f'peca de {largura} x {altura} nao encontrada')


def placa(contorno, furos, tirar=lambda x, y: False):
    """O poligono com os furos; tirar(x, y) recusa o furo pelo centro (do canto de baixo)."""
    x0, y0 = contorno.bounds[:2]
    ficam = []
    for f in furos:
        fx0, fy0, fx1, fy1 = f.bounds
        if not tirar((fx0 + fx1) / 2 - x0, (fy0 + fy1) / 2 - y0):
            ficam.append(f.exterior.coords)
    return Polygon(contorno.exterior.coords, ficam)


def do_json(nome):
    with open(os.path.join(CAD, nome)) as f:
        d = json.load(f)
    return Polygon(d['contorno'], d['furos'])


# ---------------------------------------------------------------------------
#  os frames
# ---------------------------------------------------------------------------

def circulos(poligono, raio=None):
    """Os furos redondos (cx, cy, r) de um poligono do DXF."""
    achados = []
    for anel in poligono.interiors:
        c = ajustar_circulo(list(anel.coords))
        if c and (raio is None or abs(c[2] - raio) < 0.02):
            achados.append(c)
    return achados


def drone(arquivo_do_braco):
    """A placa de baixo do Source Two com os quatro bracos parafusados nela."""
    contorno, furos = pecas_do_dxf('S2_v0.1_main_plate_3mm.dxf')[0]
    base = placa(contorno, furos)
    contorno, furos = pecas_do_dxf(arquivo_do_braco)[0]
    braco = placa(contorno, furos)

    parafuso = 1.475
    motor = max(circulos(braco), key=lambda c: c[2])
    a, b = sorted(circulos(braco, parafuso), key=lambda c: math.dist(c[:2], motor[:2]))
    passo = math.dist(a[:2], b[:2])            # o de fora (a) e o de dentro (b)

    furos_da_placa = circulos(base, parafuso)
    cy = (base.bounds[1] + base.bounds[3]) / 2
    centro = (0.0, cy)
    pares = []
    for i, p in enumerate(furos_da_placa):
        for q in furos_da_placa[i + 1:]:
            if abs(math.dist(p[:2], q[:2]) - passo) < 0.01:
                fora, dentro = sorted((p, q), key=lambda c: -math.dist(c[:2], centro))
                pares.append((fora, dentro))
    if len(pares) != 4:
        raise SystemExit(f'{arquivo_do_braco}: {len(pares)} pares de furos com {passo:.3f} mm')

    pecas = [base]
    for fora, dentro in pares:
        # giro + translacao (sem espelhar: os quatro bracos sao a mesma peca)
        ang = (math.atan2(fora[1] - dentro[1], fora[0] - dentro[0])
               - math.atan2(a[1] - b[1], a[0] - b[0]))
        c, s = math.cos(ang), math.sin(ang)
        tx = fora[0] - (c * a[0] - s * a[1])
        ty = fora[1] - (s * a[0] + c * a[1])
        pecas.append(affine_transform(braco, [c, -s, s, c, tx, ty]))
    junto = unary_union(pecas)
    if junto.geom_type != 'Polygon':
        raise SystemExit('o frame do drone nao saiu inteiro')
    return junto


def braden():
    """O painel C (o da direita) do SVG de corte: a placa de 203,2 x 304,8 mm."""
    with open(os.path.join(CAD, 'BradenAcrylicDrawing15.svg'), encoding='utf8') as f:
        texto = f.read()
    x0, largura, altura = 406.4, 203.2, 304.8
    furos = []
    for m in re.finditer(r'<(rect|circle)\b(.*?)/>', texto, re.S):
        a = {k: float(v) for k, v in re.findall(r'\s(cx|cy|r|x|y|width|height|rx)="([-0-9.]+)"', m.group(2))}
        if m.group(1) == 'circle':
            if a['cx'] < x0:
                continue
            furos.append(Polygon([(a['cx'] + a['r'] * math.cos(t), a['cy'] + a['r'] * math.sin(t))
                                  for t in (k * math.pi / 32 for k in range(64))]))
        else:
            if a['x'] < x0:
                continue
            r = a.get('rx', 0)
            furos.append(box(a['x'] + r, a['y'] + r, a['x'] + a['width'] - r, a['y'] + a['height'] - r)
                         .buffer(r, quad_segs=16) if r else box(a['x'], a['y'], a['x'] + a['width'], a['y'] + a['height']))
    furos = [translate(f, -x0, 0) for f in furos]
    return Polygon([(0, 0), (largura, 0), (largura, altura), (0, altura)], [f.exterior.coords for f in furos])


def frames():
    dearm = pecas_do_dxf('DE-Arm-4mm.dxf')
    return [
        # id, poligono, y do arquivo para cima?, material, tipo
        ('frame-2wd', do_json('2wd-dasaki-placa.json'), True, 'acrilico', 'carro'),
        ('frame-4wd', do_json('4wd-sb43201-placa.json'), True, 'acrilico-preto', 'carro'),
        ('frame-4wd-placa', braden(), False, 'acrilico', 'carro'),
        ('frame-drone-5', drone('S2_v0.1_5_inch_arms_4mm_4x.dxf'), True, 'carbono', 'drone'),
        ('frame-drone-6', drone('S2_v0.1_6_inch_arms_4mm_4x.dxf'), True, 'carbono', 'drone'),
        ('frame-braco-mearm', placa(*peca_de_tamanho(pecas_do_dxf('MeArmV3.dxf'), 145.0, 95.0)), True,
         'acrilico-laranja', 'braco'),
        # o texto "DE Arm 2025" fica nos 16 mm de baixo da placa
        ('frame-braco-dearm', placa(*peca_de_tamanho(dearm, 100.96, 114.27), tirar=lambda x, y: y < 16),
         True, 'compensado', 'braco'),
        # a letra "R", no meio da placa
        ('frame-braco-servo', placa(*peca_de_tamanho(dearm, 71.46, 59.32),
                                    tirar=lambda x, y: math.dist((x, y), (31.87, 32.87)) < 1),
         True, 'compensado', 'braco'),
        ('frame-suporte-sg90', placa(*peca_de_tamanho(dearm, 25.07, 39.64)), True, 'compensado', 'braco'),
    ]


# ---------------------------------------------------------------------------
#  o caminho
# ---------------------------------------------------------------------------

def ajustar_circulo(pts):
    """(cx, cy, r) quando o anel e um circulo (de CIRCLE achatado ou de malha), senao None."""
    if pts[0] == pts[-1]:
        pts = pts[:-1]
    if len(pts) < 12:
        return None
    n = len(pts)
    sx = sum(p[0] for p in pts) / n
    sy = sum(p[1] for p in pts) / n
    # ajuste algebrico (Kasa) em volta do centro medio
    suu = suv = svv = suuu = svvv = suvv = svuu = 0.0
    for x, y in pts:
        u, v = x - sx, y - sy
        suu += u * u; suv += u * v; svv += v * v
        suuu += u * u * u; svvv += v * v * v; suvv += u * v * v; svuu += v * u * u
    det = suu * svv - suv * suv
    if abs(det) < 1e-12:
        return None
    uc = (0.5 * (suuu + suvv) * svv - 0.5 * (svvv + svuu) * suv) / det
    vc = (0.5 * (svvv + svuu) * suu - 0.5 * (suuu + suvv) * suv) / det
    cx, cy = sx + uc, sy + vc
    r = sum(math.dist(p, (cx, cy)) for p in pts) / n
    if r <= 0 or max(abs(math.dist(p, (cx, cy)) - r) for p in pts) > max(0.02, 0.01 * r):
        return None
    area = abs(Polygon(pts).area)
    if abs(area - math.pi * r * r) > 0.05 * math.pi * r * r:
        return None
    # os angulos tem que dar a volta inteira, sem buraco (senao e um arco so)
    angs = sorted(math.atan2(y - cy, x - cx) for x, y in pts)
    maior = max(b - a for a, b in zip(angs, angs[1:] + [angs[0] + 2 * math.pi]))
    return (cx, cy, r) if maior < math.pi / 4 else None


def num(v):
    s = f'{v:.2f}'.rstrip('0').rstrip('.')
    return '0' if s in ('-0', '') else s


def caminho(poligono, y_para_cima):
    """O caminho em mm (origem no canto de cima a esquerda) e o tamanho."""
    x0, y0, x1, y1 = poligono.bounds
    largura, altura = x1 - x0, y1 - y0

    def tela(x, y):
        return (x - x0, (y1 - y) if y_para_cima else (y - y0))

    partes = []
    for anel in [poligono.exterior, *poligono.interiors]:
        pts = list(anel.coords)
        c = ajustar_circulo(pts) if anel is not poligono.exterior else None
        if c:
            cx, cy = tela(c[0], c[1])
            r = c[2]
            partes.append(f'M{num(cx - r)},{num(cy)}A{num(r)},{num(r)} 0 1 0 {num(cx + r)},{num(cy)}'
                          f'A{num(r)},{num(r)} 0 1 0 {num(cx - r)},{num(cy)}Z')
            continue
        simples = LineString(pts).simplify(0.01)
        q = [tela(x, y) for x, y in simples.coords]
        if q[0] == q[-1]:
            q = q[:-1]
        partes.append('M' + f'{num(q[0][0])},{num(q[0][1])}L'
                      + ' '.join(f'{num(x)},{num(y)}' for x, y in q[1:]) + 'Z')
    return round(largura, 2), round(altura, 2), ''.join(partes)


def main():
    saida = []
    for ident, poligono, y_para_cima, material, tipo in frames():
        largura, altura, d = caminho(poligono, y_para_cima)
        furos = len(poligono.interiors)
        print(f'{ident:20} {largura:7.2f} x {altura:7.2f} mm  {furos:3} furos  {len(d):6} letras')
        saida.append({'id': ident, 'tipo': tipo, 'material': material,
                      'mm': [largura, altura], 'caminho': d})
    with open(SAIDA, 'w', encoding='utf8', newline='\n') as f:
        f.write('[\n' + ',\n'.join(json.dumps(x, ensure_ascii=False) for x in saida) + '\n]\n')
    print('gravado', os.path.normpath(SAIDA))


if __name__ == '__main__':
    if len(sys.argv) > 1 and sys.argv[1] == '--sem-gravar':
        for ident, poligono, *_ in frames():
            print(ident, poligono.bounds, len(poligono.interiors))
    else:
        main()
