"""python ferramentas/extrair-svg/pecas-diodos.py

Os diodos (Alpha 24), axiais, com o mesmo passo de pernas do resistor:

  - diodo          : o 1N4007 (DO-41, corpo preto, faixa prateada no catodo);
                     variantes diodo-1n4148 (vidro laranja) e diodo-1n5408
                     (DO-201, maior).
  - diodo-zener    : vidro laranja com a faixa preta e a tensao escrita
                     (variantes diodo-zener-3v3, -5v1, -12v).
  - diodo-schottky : o 1N5819 (preto, faixa cinza); variante diodo-schottky-ss34.

A faixa e o catodo (C), na direita; o anodo (A) na esquerda.
"""
import io
import json

BASE = 'C:/Users/seraf/OneDrive/Desktop/SimuladorESP32/'
PX = 96 / 25.4
L, A = 15.645, 3.0
pecas = []


def gravar(nome, corpo):
    svg = ('<svg xmlns="http://www.w3.org/2000/svg" version="1.1"' + chr(10)
           + f'     width="{L}mm" height="{A}mm" viewBox="0 0 {L} {A}">' + chr(10) + corpo + chr(10) + '</svg>' + chr(10))
    io.open(BASE + f'app/assets/{nome}.svg', 'w', encoding='utf-8', newline='').write(svg)


def diodo(comprimento, diametro, corpo, faixa, texto='', cor_texto='#d9dce0', vidro=False):
    x0 = (L - comprimento) / 2
    y0 = (A - diametro) / 2
    s = f'  <rect x="0" y="{A / 2 - 0.3:.2f}" width="{L}" height="0.6" fill="#b4b8be"/>\n'
    s += (f'  <rect x="{x0:.2f}" y="{y0:.2f}" width="{comprimento:.2f}" height="{diametro:.2f}" rx="{diametro * 0.3:.2f}" '
          f'fill="{corpo}" stroke="#0b0b0c" stroke-width="0.08"{" fill-opacity=\"0.85\"" if vidro else ""}/>\n')
    if vidro:
        # o fio de dentro, visto pelo vidro
        s += f'  <rect x="{x0 + 0.4:.2f}" y="{A / 2 - 0.12:.2f}" width="{comprimento - 0.8:.2f}" height="0.24" fill="#7a5a2a" opacity="0.7"/>\n'
    s += f'  <rect x="{x0:.2f}" y="{y0 + 0.15:.2f}" width="{comprimento:.2f}" height="{diametro * 0.22:.2f}" rx="0.1" fill="#ffffff" opacity="0.18"/>\n'
    s += f'  <rect x="{x0 + comprimento * 0.80:.2f}" y="{y0:.2f}" width="{comprimento * 0.12:.2f}" height="{diametro:.2f}" fill="{faixa}"/>\n'
    if texto:
        s += (f'  <text x="{x0 + comprimento * 0.42:.2f}" y="{A / 2 + 0.28:.2f}" fill="{cor_texto}" font-size="0.75px" '
              f'text-anchor="middle" font-family="Verdana" font-weight="bold">{texto}</text>\n')
    return s


def guardar(id_peca, descricoes):
    pecas.append({
        'id': id_peca, 'largura': round(L * PX), 'altura': round(A * PX),
        'pinos': [{'nome': 'A', 'x': 0, 'y': round(A / 2 * PX, 2), 'sinais': [], 'descricao': descricoes[0]},
                  {'nome': 'C', 'x': round(L * PX - 0.2, 2), 'y': round(A / 2 * PX, 2), 'sinais': [], 'descricao': descricoes[1]}],
        'variantes': [], 'metades': False,
    })


# o comum
gravar('diodo', diodo(5.2, 2.6, '#1d1e22', '#c9ccd1', '4007'))
gravar('diodo-1n4148', diodo(3.8, 1.8, '#d9762a', '#111111', vidro=True))
gravar('diodo-1n5408', diodo(7.5, 2.9, '#1d1e22', '#c9ccd1', '5408'))
guardar('diodo', ['Anodo (A): a corrente entra por aqui', 'Catodo (C, o lado da faixa): a corrente sai por aqui'])

# o zener
for volts, sufixo in [('5V1', ''), ('3V3', '-3v3'), ('12V', '-12v')]:
    gravar('diodo-zener' + sufixo, diodo(4.2, 2.0, '#d9762a', '#111111', volts, '#3a2408', vidro=True))
guardar('diodo-zener', ['Anodo (A): ligue no GND (no regulador de zener)',
                        'Catodo (C, a faixa): o lado positivo -- de ponta-cabeca ele segura a tensao do zener'])

# o schottky
gravar('diodo-schottky', diodo(5.2, 2.6, '#1d1e22', '#8a8f98', '5819'))
gravar('diodo-schottky-ss34', diodo(5.6, 2.8, '#2a2b2f', '#8a8f98', 'SS34'))
guardar('diodo-schottky', ['Anodo (A)', 'Catodo (C, a faixa): cai so ~0,3 V (o diodo de protecao de bateria e de fonte)'])

cam = BASE + 'app/assets/pecas.json'
d = json.load(io.open(cam, encoding='utf-8'))
novos = {p['id'] for p in pecas}
d = [p for p in d if p['id'] not in novos] + pecas
d.sort(key=lambda p: p['id'])
io.open(cam, 'w', encoding='utf-8', newline='').write(json.dumps(d, ensure_ascii=False, indent=1))
print(f'catalogo: {len(d)} pecas')
