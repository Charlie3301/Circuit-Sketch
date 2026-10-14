"""python ferramentas/extrair-svg/pecas-passivos.py

Os passivos que guardam energia (Alpha 24), vistos de lado, com as pernas
para baixo no passo de 2,54 mm (encaixam na protoboard):

  - capacitor             : o ceramico de disco (o "104" laranja);
  - capacitor-eletrolitico: a lata azul com a faixa do negativo (+ e -);
  - indutor               : o indutor radial (o carretel com o fio de cobre);
  - cristal               : o cristal HC-49 (a lata de metal baixa).

O valor (100 nF, 470 uF, 100 uH, 16 MHz) e escolhido na barra da peca.
"""
import io
import json

BASE = 'C:/Users/seraf/OneDrive/Desktop/SimuladorESP32/'
PX = 96 / 25.4
PASSO = 2.54
pecas = []


def gravar(nome, l, a, corpo):
    svg = ('<svg xmlns="http://www.w3.org/2000/svg" version="1.1"' + chr(10)
           + f'     width="{l}mm" height="{a}mm" viewBox="0 0 {l} {a}">' + chr(10) + corpo + chr(10) + '</svg>' + chr(10))
    io.open(BASE + f'app/assets/{nome}.svg', 'w', encoding='utf-8', newline='').write(svg)


def pernas(xs, y0, y1):
    return ''.join(f'  <rect x="{x - 0.25:.2f}" y="{y0:.2f}" width="0.5" height="{y1 - y0:.2f}" fill="#b4b8be"/>\n' for x in xs)


def texto(x, y, t, tam, cor):
    return (f'  <text x="{x:.2f}" y="{y:.2f}" fill="{cor}" font-size="{tam}px" text-anchor="middle" '
            f'font-family="Verdana" font-weight="bold">{t}</text>\n')


def guardar(id_peca, l, a, pinos):
    pecas.append({
        'id': id_peca, 'largura': round(l * PX), 'altura': round(a * PX),
        'pinos': [{'nome': n, 'x': round(x * PX, 2), 'y': round(y * PX, 2), 'sinais': [], 'descricao': d} for n, x, y, d in pinos],
        'variantes': [], 'metades': False,
    })


# ---- ceramico de disco ----
l, a = 6.0, 10.0
xs = [l / 2 - PASSO / 2, l / 2 + PASSO / 2]
corpo = pernas(xs, 4.0, a - 0.3)
corpo += ('  <ellipse cx="3.0" cy="3.0" rx="2.8" ry="2.9" fill="#e07a1f" stroke="#7a3e08" stroke-width="0.15"/>\n'
          '  <ellipse cx="2.3" cy="2.1" rx="1.1" ry="0.8" fill="#ffffff" opacity="0.25"/>\n')
corpo += texto(3.0, 3.4, '104', 1.2, '#4a2404')
gravar('capacitor', l, a, corpo)
guardar('capacitor', l, a, [('1', xs[0], a - 0.3, 'Terminal (o ceramico nao tem polaridade)'),
                            ('2', xs[1], a - 0.3, 'Terminal')])

# ---- eletrolitico ----
l, a = 6.6, 15.0
xs = [l / 2 - PASSO / 2, l / 2 + PASSO / 2]
corpo = pernas(xs, 11.0, a - 0.3)
corpo += ('  <rect x="0.15" y="0.3" width="6.3" height="11.0" rx="0.8" fill="#1f4fa8" stroke="#0e2a5c" stroke-width="0.15"/>\n'
          '  <rect x="0.15" y="0.3" width="6.3" height="0.9" rx="0.4" fill="#b9bec6"/>\n'
          '  <rect x="4.4" y="1.2" width="1.6" height="9.6" fill="#c9d4ea"/>\n'
          '  <rect x="0.6" y="1.5" width="1.0" height="9.0" fill="#ffffff" opacity="0.18"/>\n')
for y in (3.0, 5.6, 8.2):
    corpo += texto(5.2, y, '-', 1.6, '#1f4fa8')
corpo += texto(2.6, 6.6, 'µF', 1.3, '#dfe7f7')
gravar('capacitor-eletrolitico', l, a, corpo)
guardar('capacitor-eletrolitico', l, a, [('+', xs[0], a - 0.3, 'Positivo (a perna comprida): no lado de maior tensao'),
                                         ('-', xs[1], a - 0.3, 'Negativo (o lado da faixa): no GND. Ao contrario ele esquenta e estoura')])

# ---- indutor radial ----
l, a = 8.0, 12.0
xs = [l / 2 - PASSO, l / 2 + PASSO]
corpo = pernas(xs, 8.0, a - 0.3)
corpo += ('  <rect x="0.4" y="0.6" width="7.2" height="1.3" rx="0.4" fill="#5a5d64" stroke="#2a2b2f" stroke-width="0.12"/>\n'
          '  <rect x="0.4" y="6.6" width="7.2" height="1.3" rx="0.4" fill="#5a5d64" stroke="#2a2b2f" stroke-width="0.12"/>\n'
          '  <rect x="1.0" y="1.9" width="6.0" height="4.7" fill="#b8742e"/>\n')
for y in [2.1 + 0.42 * i for i in range(11)]:
    corpo += f'  <rect x="1.0" y="{y:.2f}" width="6.0" height="0.22" fill="#d9944a"/>\n'
gravar('indutor', l, a, corpo)
guardar('indutor', l, a, [('1', xs[0], a - 0.3, 'Terminal'), ('2', xs[1], a - 0.3, 'Terminal')])

# ---- cristal HC-49 ----
l, a = 11.0, 10.0
xs = [l / 2 - 2.44, l / 2 + 2.44]
corpo = pernas(xs, 4.0, a - 0.3)
corpo += ('  <rect x="0.3" y="0.3" width="10.4" height="3.9" rx="1.9" fill="#c9ccd1" stroke="#80858c" stroke-width="0.15"/>\n'
          '  <rect x="1.0" y="0.7" width="9.0" height="0.8" rx="0.4" fill="#ffffff" opacity="0.4"/>\n')
corpo += texto(5.5, 3.1, '16.000', 1.3, '#3a3d42')
gravar('cristal', l, a, corpo)
guardar('cristal', l, a, [('1', xs[0], a - 0.3, 'Terminal (vai num pino XTAL do microcontrolador, com um capacitor de 22 pF ao GND)'),
                          ('2', xs[1], a - 0.3, 'Terminal')])

cam = BASE + 'app/assets/pecas.json'
d = json.load(io.open(cam, encoding='utf-8'))
novos = {p['id'] for p in pecas}
d = [p for p in d if p['id'] not in novos] + pecas
d.sort(key=lambda p: p['id'])
io.open(cam, 'w', encoding='utf-8', newline='').write(json.dumps(d, ensure_ascii=False, indent=1))
print(f'catalogo: {len(d)} pecas')
