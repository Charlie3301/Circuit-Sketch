"""python ferramentas/extrair-svg/pecas-chaves.py

A chave fim de curso (micro switch, a KW11-3Z com alavanca e roldana) da
Alpha 23, vista de lado: o corpo preto, o pino do botao, a alavanca de metal
presa na dobradica da esquerda com a roldana na ponta, e os tres terminais
embaixo -- COM, NC (fechado com a alavanca solta) e NO (fechado apertada).

A alavanca e a parte que se mexe ("-movel"): gira em torno da dobradica
quando o clique aperta a chave.
"""
import io
import json

BASE = 'C:/Users/seraf/OneDrive/Desktop/SimuladorESP32/'
PX = 96 / 25.4
PASSO = 2.54

pecas = []


def gravar_svg(nome, svg):
    io.open(BASE + f'app/assets/{nome}.svg', 'w', encoding='utf-8', newline='').write(svg)


def guardar(id_peca, larg_mm, alt_mm, pinos):
    pecas.append({
        'id': id_peca,
        'largura': round(larg_mm * PX),
        'altura': round(alt_mm * PX),
        'pinos': [{'nome': n, 'x': round(x * PX, 2), 'y': round(y * PX, 2),
                   'sinais': s, 'descricao': d}
                  for n, x, y, s, d in pinos],
        'variantes': [], 'metades': False,
    })
    print(f'{id_peca}: {round(larg_mm * PX)}x{round(alt_mm * PX)} px, {len(pinos)} pinos')


l, a = 28.0, 19.0
dx, dy = 5.0, 7.2                       # a dobradica da alavanca
xs = [6.4, 6.4 + 3 * PASSO, 6.4 + 6 * PASSO]   # COM, NC, NO (7,62 mm entre eles)
y_pino = a - 0.9
nomes = ['COM', 'NC', 'NO']

cab = (f'<svg xmlns="http://www.w3.org/2000/svg" version="1.1"\n'
       f'     width="{l}mm" height="{a}mm" viewBox="0 0 {l} {a}">\n')

corpo = ''
for x in xs:
    corpo += (f'  <rect x="{x - 1.1:.2f}" y="15.5" width="2.2" height="{y_pino - 15.0:.2f}" fill="#c9a24a" stroke="#8a6d1f" stroke-width="0.2"/>\n'
              f'  <circle cx="{x:.2f}" cy="{y_pino - 1.2:.2f}" r="0.55" fill="#2a2a2e"/>\n')
corpo += f'''  <rect x="2.6" y="8.0" width="22.8" height="8.6" rx="0.6" fill="#232428" stroke="#0e0f11" stroke-width="0.3"/>
  <circle cx="7.4" cy="12.3" r="1.25" fill="#0e0f11" stroke="#3a3c41" stroke-width="0.25"/>
  <circle cx="20.6" cy="12.3" r="1.25" fill="#0e0f11" stroke="#3a3c41" stroke-width="0.25"/>
  <!-- o pino do botao, que a alavanca empurra -->
  <rect x="15.6" y="6.6" width="2.6" height="1.6" rx="0.4" fill="#d23b33"/>
  <!-- a dobradica -->
  <rect x="3.2" y="6.4" width="3.6" height="1.8" rx="0.4" fill="#9aa0a8" stroke="#5a6068" stroke-width="0.2"/>
  <text x="{xs[0]:.2f}" y="15.4" fill="#d9d9d9" font-size="1.4px" text-anchor="middle" font-family="Verdana" font-weight="bold">C</text>
  <text x="{xs[1]:.2f}" y="15.4" fill="#d9d9d9" font-size="1.4px" text-anchor="middle" font-family="Verdana" font-weight="bold">NC</text>
  <text x="{xs[2]:.2f}" y="15.4" fill="#d9d9d9" font-size="1.4px" text-anchor="middle" font-family="Verdana" font-weight="bold">NO</text>'''

movel = f'''  <!-- a alavanca de metal, presa na dobradica, e a roldana na ponta -->
  <path d="M{dx:.2f} {dy - 0.45:.2f} L23.6 {dy - 2.9:.2f} L23.8 {dy - 2.1:.2f} L{dx:.2f} {dy + 0.45:.2f} Z" fill="#c9cdd3" stroke="#6d737c" stroke-width="0.25"/>
  <circle cx="{dx:.2f}" cy="{dy:.2f}" r="0.75" fill="#7d838c"/>
  <circle cx="24.2" cy="{dy - 4.4:.2f}" r="2.1" fill="#e8e8e2" stroke="#8f8f8a" stroke-width="0.3"/>
  <circle cx="24.2" cy="{dy - 4.4:.2f}" r="0.55" fill="#7d838c"/>'''

gravar_svg('micro-switch', f'{cab}{corpo}\n{movel}\n</svg>\n')
gravar_svg('micro-switch-corpo', f'{cab}{corpo}\n</svg>\n')
gravar_svg('micro-switch-movel', f'{cab}{movel}\n</svg>\n')
print(f'  dobradica: fracao ({dx / l:.5f}, {dy / a:.5f}); capa em ({16.0 * PX:.1f}, {4.6 * PX:.1f}) px')

desc = {
    'COM': 'Comum: o que a chave liga ao NC (solta) ou ao NO (apertada). Em geral, o GND',
    'NC': 'Normalmente fechado: ligado ao COM com a alavanca solta',
    'NO': 'Normalmente aberto: ligado ao COM so com a alavanca apertada (num GPIO com INPUT_PULLUP, le LOW apertada)',
}
guardar('micro-switch', l, a, [(n, x, y_pino, [], desc[n]) for n, x in zip(nomes, xs)])

cam = BASE + 'app/assets/pecas.json'
d = json.load(io.open(cam, encoding='utf-8'))
novos = {p['id'] for p in pecas}
d = [p for p in d if p['id'] not in novos] + pecas
d.sort(key=lambda p: p['id'])
io.open(cam, 'w', encoding='utf-8', newline='').write(json.dumps(d, ensure_ascii=False, indent=1))
print(f'\ncatalogo: {len(d)} pecas')
