"""Tira do desenho mecanico da Nucleo-32 (UM1956, figura 5, vetorial) cada forma
dentro da placa, em mm a partir do canto de cima-esquerdo. A escala sai do passo
dos furos (2,54 mm). uso: nucleo32_layout.py um1956.pdf stm32-cad/nucleo32.json saida.png"""
import json
import sys

import pymupdf as fitz

doc = fitz.open(sys.argv[1])
pg = doc[12]
dr = pg.get_drawings()

# o contorno da placa: as quatro linhas grossas (1 pt) de mais de 120 pt
xs_, ys_ = [], []
for d in dr:
    for it in d['items']:
        if it[0] == 'l' and (d.get('width') or 0) > 0.9:
            p, q = it[1], it[2]
            if abs(p.x - q.x) < 1 and abs(p.y - q.y) > 300 or abs(p.y - q.y) < 1 and abs(p.x - q.x) > 120:
                xs_ += [p.x, q.x]; ys_ += [p.y, q.y]
R = fitz.Rect(min(xs_), min(ys_), max(xs_), max(ys_))
print('placa', R, R.height / R.width, 50.29 / 18.54)

k = 18.54 / R.width                 # a cota da figura: 730 mil (18,54 mm)
print('mm por ponto', k, 'placa mm', R.width * k, R.height * k)

saida = []
for d in dr:
    r = d['rect']
    if not (r.x0 >= R.x0 - 8 and r.x1 <= R.x1 + 8 and r.y0 >= R.y0 - 20 and r.y1 <= R.y1 + 8):
        continue
    saida.append({
        'cheio': d.get('fill') is not None, 'traco': d.get('color') is not None, 'larg': d.get('width'),
        'x0': round((r.x0 - R.x0) * k, 3), 'y0': round((r.y0 - R.y0) * k, 3),
        'x1': round((r.x1 - R.x0) * k, 3), 'y1': round((r.y1 - R.y0) * k, 3),
        'tipos': ''.join(i[0] for i in d['items'])[:40],
    })
textos = []
for b in pg.get_text('dict')['blocks']:
    for ln in b.get('lines', []):
        for sp in ln['spans']:
            x0, y0, x1, y1 = sp['bbox']
            if R.x0 - 8 <= x0 and x1 <= R.x1 + 8 and R.y0 - 20 <= y0 and y1 <= R.y1 + 8:
                textos.append({'t': sp['text'], 'x0': round((x0 - R.x0) * k, 2), 'y0': round((y0 - R.y0) * k, 2),
                               'x1': round((x1 - R.x0) * k, 2), 'y1': round((y1 - R.y0) * k, 2), 'dir': ln['dir']})
json.dump({'mm_por_ponto': k, 'placa_mm': [R.width * k, R.height * k], 'desenhos': saida, 'textos': textos},
          open(sys.argv[2], 'w', encoding='utf-8'), indent=0)
print(len(saida), 'desenhos,', len(textos), 'textos')
clip = fitz.Rect(R.x0 - 10, R.y0 - 25, R.x1 + 10, R.y1 + 10)
pg.get_pixmap(dpi=600, clip=clip).save(sys.argv[3])
