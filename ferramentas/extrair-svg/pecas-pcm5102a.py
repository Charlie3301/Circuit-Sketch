"""python ferramentas/extrair-svg/pecas-pcm5102a.py

O modulo PCM5102A (Alpha 24): a plaquinha roxa do DAC I2S estereo da Texas,
com a saida de fone (P2 de 3,5 mm) em cima e o conector de 6 pinos embaixo
(VIN, GND, LCK, DIN, BCK, SCK -- a ordem da serigrafia do GY-PCM5102). Os
jumpers de solda do verso (FLT, DEMP, XSMT, FMT) vem fechados de fabrica:
filtro normal, sem de-enfase, saida ligada, formato I2S. O SCK vai no GND
(o chip tira o clock do BCK pelo PLL dele).
"""
import io
import json

BASE = 'C:/Users/seraf/OneDrive/Desktop/SimuladorESP32/'
PX = 96 / 25.4
PASSO = 2.54

L, A = 32.0, 22.0
y_pino = A - 2.6
x0 = (L - 5 * PASSO) / 2
nomes = ['VIN', 'GND', 'LCK', 'DIN', 'BCK', 'SCK']


def texto(x, y, t, tam=1.2, cor='#f4ecff', ancora='middle', peso='bold'):
    return (f'  <text x="{x:.2f}" y="{y:.2f}" fill="{cor}" font-size="{tam}px" text-anchor="{ancora}" '
            f'font-family="Verdana" font-weight="{peso}">{t}</text>\n')


corpo = (f'  <rect x="0.3" y="0.3" width="{L - 0.6}" height="{A - 0.6}" rx="1.3" fill="#5b2d8e" stroke="#341a55" stroke-width="0.3"/>\n'
         f'  <circle cx="2.2" cy="2.2" r="1.3" fill="#1e0f30"/><circle cx="{L - 2.2}" cy="2.2" r="1.3" fill="#1e0f30"/>\n')
# o P2 (fone): o corpo preto e a boca prateada, saindo pela borda de cima
corpo += (f'  <rect x="{L / 2 - 3.6:.2f}" y="0.0" width="7.2" height="7.6" rx="0.6" fill="#18181b" stroke="#000" stroke-width="0.2"/>\n'
          f'  <circle cx="{L / 2:.2f}" cy="1.6" r="2.3" fill="#c9cdd3" stroke="#6d737c" stroke-width="0.3"/>\n'
          f'  <circle cx="{L / 2:.2f}" cy="1.6" r="1.2" fill="#2a2b2f"/>\n')
corpo += texto(L / 2 + 5.4, 3.6, 'LINE OUT', 0.9, ancora='start')
# o PCM5102A (TSSOP-20) e os capacitores
cx, cy = L / 2, 11.0
corpo += f'  <rect x="{cx - 3.3:.2f}" y="{cy - 2.2:.2f}" width="6.6" height="4.4" rx="0.3" fill="#1b1b1d"/>\n'
for i in range(10):
    for lado in (-1, 1):
        corpo += f'  <rect x="{cx - 3.0 + i * 0.66:.2f}" y="{cy + lado * 2.2 - (0.6 if lado < 0 else 0):.2f}" width="0.3" height="0.6" fill="#c9cdd3"/>\n'
corpo += texto(cx, cy + 0.4, 'PCM5102A', 0.95, '#d9d9df')
for k, (x, y) in enumerate([(5.0, 9.0), (5.0, 12.4), (L - 6.4, 9.0), (L - 6.4, 12.4), (8.4, 6.2), (L - 9.8, 6.2)]):
    corpo += f'  <rect x="{x:.2f}" y="{y:.2f}" width="1.4" height="2.2" rx="0.2" fill="#b98c3a" stroke="#6a4d18" stroke-width="0.15"/>\n'
corpo += f'  <rect x="{L / 2 - 1.6:.2f}" y="15.0" width="3.2" height="1.6" rx="0.3" fill="#1b1b1d"/>\n'   # o regulador de 3,3 V
# o conector
for i, n in enumerate(nomes):
    x = x0 + i * PASSO
    corpo += (f'  <rect x="{x - 1.2:.2f}" y="{y_pino - 1.2:.2f}" width="2.4" height="2.4" fill="#1b1b1d"/>\n'
              f'  <rect x="{x - 0.38:.2f}" y="{y_pino - 0.38:.2f}" width="0.76" height="0.76" fill="#d9b44a"/>\n')
    corpo += texto(x, y_pino - 2.0, n, 0.85)

svg = (f'<svg xmlns="http://www.w3.org/2000/svg" version="1.1"\n'
       f'     width="{L}mm" height="{A}mm" viewBox="0 0 {L} {A}">\n{corpo}</svg>\n')
io.open(BASE + 'app/assets/pcm5102a.svg', 'w', encoding='utf-8', newline='').write(svg)

descricao = {
    'VIN': 'VIN: 3,3 a 5 V (o regulador da placa faz os 3,3 V do chip)',
    'GND': 'GND: o GND da placa',
    'LCK': 'LCK (LRCK, WS): o canal da vez -- o pino de WS do I2S da placa',
    'DIN': 'DIN: os dados do audio -- o pino de saida de dados do I2S da placa (DOUT)',
    'BCK': 'BCK: o clock dos bits -- o pino de BCLK do I2S da placa',
    'SCK': 'SCK: o clock mestre. Ligue no GND: o chip tira o clock do BCK sozinho',
}
peca = {
    'id': 'pcm5102a', 'largura': round(L * PX), 'altura': round(A * PX),
    'pinos': [{'nome': n, 'x': round((x0 + i * PASSO) * PX, 2), 'y': round(y_pino * PX, 2),
               'sinais': ['power'] if n in ('VIN', 'GND') else [], 'descricao': descricao[n]}
              for i, n in enumerate(nomes)],
    'variantes': [], 'metades': False,
}
cam = BASE + 'app/assets/pecas.json'
d = json.load(io.open(cam, encoding='utf-8'))
d = [p for p in d if p['id'] != 'pcm5102a'] + [peca]
d.sort(key=lambda p: p['id'])
io.open(cam, 'w', encoding='utf-8', newline='').write(json.dumps(d, ensure_ascii=False, indent=1))
print(f"pcm5102a: {peca['largura']}x{peca['altura']} px; catalogo: {len(d)} pecas")
