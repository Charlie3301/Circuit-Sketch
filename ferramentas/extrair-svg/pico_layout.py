"""Tira do desenho oficial da pinagem (o SVG da documentacao da Raspberry Pi,
CC BY-SA 4.0) o lugar e o tamanho de cada coisa da placa, em mm a partir do
canto de cima-esquerdo da placa. uso: pico_layout.py placa_i.svg saida.json (o placa_i.svg sai do pico_svg_inline.py)"""
import json
import sys
from collections import Counter

import fitz

doc = fitz.open(sys.argv[1])
pg = doc[0]
dr = pg.get_drawings()


def hexa(c):
    return None if c is None else '#%02x%02x%02x' % tuple(int(round(v * 255)) for v in c)


# a placa: o maior retangulo de verde escuro
cands = [d for d in dr if d.get('fill') and d['rect'].width > 100 and d['rect'].height > 300]
placa = max(cands, key=lambda d: d['rect'].width * d['rect'].height)
R = placa['rect']
print('placa', R, hexa(placa['fill']), R.height / R.width)

# as ilhas castelladas (a cor caqui) na borda da esquerda: o passo de 2,54 mm
dentro = [d for d in dr if d['rect'].x0 >= R.x0 - 10 and d['rect'].x1 <= R.x1 + 10 and d['rect'].y0 >= R.y0 - 40 and d['rect'].y1 <= R.y1 + 40]
cores = Counter(hexa(d.get('fill')) for d in dentro)
print(cores.most_common(20))
caqui = [d for d in dentro if hexa(d.get('fill')) in ('#9a916c', '#99906c', '#958863', '#948763')]
esq = sorted([d['rect'] for d in caqui if d['rect'].x0 < R.x0 + 0.15 * R.width and d['rect'].height < 20], key=lambda r: r.y0)
ys = [(r.y0 + r.y1) / 2 for r in esq]
print('ilhas da esquerda', len(esq), [round(y, 1) for y in ys[:5]])
passos = [b - a for a, b in zip(ys, ys[1:])]
passo = sorted(passos)[len(passos) // 2] if passos else 1
k = 2.54 / passo
print('mm por ponto', k, 'placa em mm', R.width * k, R.height * k)

saida = []
for d in dentro:
    r = d['rect']
    saida.append({
        'cor': hexa(d.get('fill')), 'traco': hexa(d.get('color')),
        'x0': round((r.x0 - R.x0) * k, 3), 'y0': round((r.y0 - R.y0) * k, 3),
        'x1': round((r.x1 - R.x0) * k, 3), 'y1': round((r.y1 - R.y0) * k, 3),
        'itens': len(d['items']), 'tipos': ''.join(i[0] for i in d['items'])[:40],
    })
json.dump({'mm_por_ponto': k, 'placa_mm': [R.width * k, R.height * k], 'desenhos': saida},
          open(sys.argv[2], 'w', encoding='utf-8'), indent=0)
print(len(saida), 'desenhos')
