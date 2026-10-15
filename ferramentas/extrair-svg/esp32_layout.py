"""python -I esp32_layout.py layout.pdf saida.json

Tira do PDF de layout de uma DevKit da Espressif (o "PCB layout" da pagina
de cada placa em docs.espressif.com) so os FATOS de montagem, em mm, com a
origem no canto de baixo-esquerdo da placa e y para cima:

  - o tamanho da placa;
  - o centro de cada ilha de cada componente de cima (o PDF traz, como
    texto, "C10x1" no centro da ilha 1 do C10);
  - os textos da serigrafia de cima (os nomes dos pinos, BOOT, RESET,
    RGB@IO48...) com o tamanho e o angulo, e o designador de cada peca.

Nao sai daqui nenhuma trilha, nem o desenho da serigrafia: o placas-esp32.py
desenha cada peca no proprio estilo, so no lugar e no tamanho de verdade.
Precisa do PyMuPDF (pip install pymupdf).
"""
import json
import math
import re
import sys

import fitz

ILHA = re.compile(r'^([A-Z]+\d+)x(\d+)$')
DESIGNADOR = re.compile(r'^[A-Z]{1,3}\d+$')


def palavras(pg):
    return [(w[4], (w[0] + w[2]) / 2, (w[1] + w[3]) / 2) for w in pg.get_text('words')]


def main(pdf, saida):
    doc = fitz.open(pdf)
    # a pagina da montagem de cima: a com mais designadores soltos
    k = max(range(len(doc)), key=lambda i: sum(1 for w in palavras(doc[i]) if DESIGNADOR.match(w[0])))
    pg = doc[k]
    ilhas = {}
    for t, x, y in palavras(pg):
        m = ILHA.match(t)
        if m:
            ilhas.setdefault(m.group(1), {})[int(m.group(2))] = (x, y)

    # a escala pelas barras J1 e J3 (passo de 2,54 mm)
    j1 = [ilhas['J1'][n] for n in sorted(ilhas['J1'])]
    s = (len(j1) - 1) * 2.54 / math.hypot(j1[-1][0] - j1[0][0], j1[-1][1] - j1[0][1])
    # o contorno: o desenho com a largura da placa (25,4 mm)
    r = max((dd['rect'] for dd in pg.get_drawings()
             if abs(min(dd['rect'].width, dd['rect'].height) * s - 25.4) < 0.4
             and max(dd['rect'].width, dd['rect'].height) * s > 30), key=lambda r: r.width * r.height)
    # o x pela barra (J1 a 1,27 mm da borda); o y pelo contorno
    dx = 1.27 - (j1[0][0] - r.x0) * s

    def mm(x, y):
        return [round((x - r.x0) * s + dx, 3), round((r.y1 - y) * s, 3)]

    placa = [25.4, round(r.height * s, 2)]
    dentro = lambda X, Y: -2 < X < 27.4 and -2 < Y < placa[1] + 8
    saida_ = {'placa': placa, 'ilhas': {}, 'textos': [], 'designadores': {}}
    for ref, ps in sorted(ilhas.items()):
        saida_['ilhas'][ref] = {str(n): mm(*p) for n, p in sorted(ps.items())}

    vistos = {}
    for b in pg.get_text('dict')['blocks']:
        for ln in b.get('lines', []):
            ang = round(math.degrees(math.atan2(-ln['dir'][1], ln['dir'][0])))
            for sp in ln['spans']:
                t = sp['text'].strip()
                bx = sp['bbox']
                X, Y = mm((bx[0] + bx[2]) / 2, (bx[1] + bx[3]) / 2)
                if not t or ILHA.match(t) or not dentro(X, Y) or 'mm' in t or 'ANT-TYPE' in t:
                    continue
                tam = round(sp['size'] * s, 3)
                if DESIGNADOR.match(t):
                    # o mesmo designador aparece tres vezes: o da serigrafia e
                    # o menor; os outros dois sao a legenda da montagem
                    if t not in vistos or tam < vistos[t]['tam']:
                        vistos[t] = {'x': X, 'y': Y, 'tam': tam, 'ang': ang}
                else:
                    saida_['textos'].append({'t': t, 'x': X, 'y': Y, 'tam': tam, 'ang': ang})
    saida_['designadores'] = dict(sorted(vistos.items()))
    # os textos repetidos (a legenda da montagem repete alguns)
    unicos, chaves = [], set()
    for t in saida_['textos']:
        c = (t['t'], round(t['x'], 1), round(t['y'], 1))
        if c not in chaves:
            chaves.add(c)
            unicos.append(t)
    saida_['textos'] = unicos
    json.dump(saida_, open(saida, 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
    print(f'{pdf}: pagina {k}, {placa[0]} x {placa[1]} mm, {len(ilhas)} pecas, {len(unicos)} textos')


if __name__ == '__main__':
    main(*sys.argv[1:3])
