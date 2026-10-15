"""Troca as classes CSS (.stN{...}) do SVG do Illustrator por style= em cada
elemento, para o MuPDF desenhar com as cores, e grava um PNG.
uso: svg_inline.py entrada.svg saida.svg saida.png [dpi]"""
import re
import sys

import fitz

ent, sai_svg, sai_png = sys.argv[1:4]
dpi = int(sys.argv[4]) if len(sys.argv) > 4 else 200
s = open(ent, encoding='utf-8').read()
estilos = dict(re.findall(r'\.(st\d+)\{([^}]*)\}', s))


def troca(m):
    classes = m.group(1).split()
    css = ';'.join(estilos.get(c, '') for c in classes)
    return f'style="{css}"'


s = re.sub(r'class="([^"]+)"', troca, s)
s = re.sub(r'<style[^>]*>.*?</style>', '', s, flags=re.S)
open(sai_svg, 'w', encoding='utf-8').write(s)
doc = fitz.open(sai_svg)
pix = doc[0].get_pixmap(dpi=dpi)
pix.save(sai_png)
print(pix.width, pix.height)
