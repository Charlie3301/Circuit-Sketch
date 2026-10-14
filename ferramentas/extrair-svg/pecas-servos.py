"""python ferramentas/extrair-svg/pecas-servos.py

Os servos novos da Alpha 23 (o micro servo SG90 continua sendo o "servo"
do Wokwi):

  - servo-mg996r : o servo grande de engrenagem de metal (9,4 kg.cm), visto
                   de cima, com as orelhas de fixacao e o eixo perto de uma
                   ponta. O braco branco e desenhado pelo editor por cima
                   (como no SG90), girando no eixo.
  - servo-360    : o micro servo de rotacao continua (FS90R): 1500 us para,
                   1000/2000 us giram para um lado e para o outro. Gira o
                   disco de tres furos preso no eixo (a parte "-movel").

Os tres fios saem pela esquerda, na mesma ordem do servo do Wokwi: GND
(marrom), V+ (vermelho) e PWM (laranja), no passo de 0,1".
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


def cabeca(l, a):
    return (f'<svg xmlns="http://www.w3.org/2000/svg" version="1.1"\n'
            f'     width="{l}mm" height="{a}mm" viewBox="0 0 {l} {a}">\n')


def fios_do_servo(x_corpo, y_meio, y_pinos):
    """Os tres fios do corpo ate os pinos da esquerda (x = 1,5 mm)."""
    corpo = ''
    for (cor, borda), y_fim, dy in zip([('#7a4a2a', '#3d2515'), ('#d23b33', '#7d1f1a'), ('#f08a24', '#8a4a10')],
                                       y_pinos, (-0.9, 0.0, 0.9)):
        d = f'M{x_corpo:.2f} {y_meio + dy:.2f} C{x_corpo - 6:.2f} {y_meio + dy:.2f} 6.0 {y_fim:.2f} 1.5 {y_fim:.2f}'
        corpo += (f'  <path d="{d}" fill="none" stroke="{borda}" stroke-width="1.0" stroke-linecap="round"/>\n'
                  f'  <path d="{d}" fill="none" stroke="{cor}" stroke-width="0.7" stroke-linecap="round"/>\n')
    for y in y_pinos:
        corpo += (f'  <circle cx="1.5" cy="{y:.2f}" r="0.75" fill="#d9b44a" stroke="#8a6d1f" stroke-width="0.2"/>\n'
                  f'  <circle cx="1.5" cy="{y:.2f}" r="0.32" fill="#2a2a2a"/>\n')
    return corpo


desc = {
    'GND': 'Fio marrom: GND (o mesmo GND da placa)',
    'V+': 'Fio vermelho: alimentacao, 4,8 a 6 V (o 5V/VIN da placa ou uma fonte; nunca um GPIO)',
    'PWM': 'Fio laranja: o sinal, um pulso a cada 20 ms (o GPIO do Servo.attach)',
}

# =====================================================================
#  MG996R
# =====================================================================
l, a = 72.0, 26.0
x0, x1 = 18.0, 58.6             # o corpo (40,6 mm)
y0, y1 = 3.0, 23.0
ex, ey = 48.0, 13.0             # o eixo (estriado), perto da ponta direita
ys = [a / 2 - PASSO, a / 2, a / 2 + PASSO]
corpo = fios_do_servo(x0, a / 2, ys)
corpo += f'''  <!-- as orelhas de fixacao -->
  <rect x="{x0 - 6.8:.2f}" y="8.2" width="{x1 - x0 + 13.6:.2f}" height="9.6" rx="1.0" fill="#1e1f22" stroke="#0b0b0c" stroke-width="0.3"/>
  <circle cx="{x0 - 4.0:.2f}" cy="10.6" r="1.0" fill="#3a3c41"/><circle cx="{x0 - 4.0:.2f}" cy="15.4" r="1.0" fill="#3a3c41"/>
  <circle cx="{x1 + 4.0:.2f}" cy="10.6" r="1.0" fill="#3a3c41"/><circle cx="{x1 + 4.0:.2f}" cy="15.4" r="1.0" fill="#3a3c41"/>
  <!-- o corpo -->
  <rect x="{x0}" y="{y0}" width="{x1 - x0:.2f}" height="{y1 - y0}" rx="1.2" fill="#2a2b2f" stroke="#0b0b0c" stroke-width="0.35"/>
  <rect x="{x0 + 1.2:.2f}" y="{y0 + 1.2:.2f}" width="{x1 - x0 - 2.4:.2f}" height="{y1 - y0 - 2.4:.2f}" rx="0.8" fill="#323338"/>
  <text x="{x0 + 11.5:.2f}" y="{ey + 0.9:.2f}" fill="#e6e6e6" font-size="2.6px" text-anchor="middle" font-family="Verdana" font-weight="bold">MG996R</text>
  <text x="{x0 + 11.5:.2f}" y="{ey + 3.6:.2f}" fill="#9aa0a8" font-size="1.4px" text-anchor="middle" font-family="Verdana">metal gear</text>
  <!-- a saida do eixo: o ressalto e o estriado -->
  <circle cx="{ex}" cy="{ey}" r="5.4" fill="#1e1f22" stroke="#0b0b0c" stroke-width="0.3"/>
  <circle cx="{ex}" cy="{ey}" r="2.6" fill="#c9cdd3" stroke="#6d737c" stroke-width="0.3"/>
  <circle cx="{ex}" cy="{ey}" r="1.0" fill="#6d737c"/>'''
io.open(BASE + 'app/assets/servo-mg996r.svg', 'w', encoding='utf-8', newline='').write(f'{cabeca(l, a)}{corpo}\n</svg>\n')
print(f'  MG996R: eixo em ({ex * PX:.1f}, {ey * PX:.1f}) px')
guardar('servo-mg996r', l, a, [(n, 1.5, y, ['power'] if n != 'PWM' else [], desc[n]) for n, y in zip(['GND', 'V+', 'PWM'], ys)])


# =====================================================================
#  Servo de rotacao continua (FS90R)
# =====================================================================
l, a = 48.0, 24.0
x0, x1 = 16.0, 39.0             # o corpo do micro servo (23 mm)
ex, ey = 33.0, 12.0
ys = [a / 2 - PASSO, a / 2, a / 2 + PASSO]
corpo = fios_do_servo(x0, a / 2, ys)
corpo += f'''  <rect x="{x0 - 4.5:.2f}" y="8.6" width="{x1 - x0 + 9.0:.2f}" height="6.8" rx="0.8" fill="#1f5fb4" stroke="#123e7a" stroke-width="0.3"/>
  <circle cx="{x0 - 2.4:.2f}" cy="12.0" r="0.85" fill="#123e7a"/><circle cx="{x1 + 2.4:.2f}" cy="12.0" r="0.85" fill="#123e7a"/>
  <rect x="{x0}" y="5.9" width="{x1 - x0}" height="12.2" rx="0.9" fill="#2468c8" stroke="#123e7a" stroke-width="0.35"/>
  <text x="{x0 + 7.0:.2f}" y="12.6" fill="#e6efff" font-size="2.0px" text-anchor="middle" font-family="Verdana" font-weight="bold">360°</text>
  <text x="{x0 + 7.0:.2f}" y="15.0" fill="#bcd2f5" font-size="1.2px" text-anchor="middle" font-family="Verdana">FS90R</text>'''
movel = f'''  <!-- o disco de tres furos no eixo (gira sem parar) -->
  <circle cx="{ex}" cy="{ey}" r="7.2" fill="#f2f2ee" stroke="#8f8f8a" stroke-width="0.3"/>
  <circle cx="{ex}" cy="{ey - 4.6:.2f}" r="0.9" fill="#b9b9b2"/>
  <circle cx="{ex + 4.0:.2f}" cy="{ey + 2.3:.2f}" r="0.9" fill="#b9b9b2"/>
  <circle cx="{ex - 4.0:.2f}" cy="{ey + 2.3:.2f}" r="0.9" fill="#b9b9b2"/>
  <path d="M{ex} {ey} L{ex} {ey - 6.8:.2f}" stroke="#d23b33" stroke-width="0.8" stroke-linecap="round"/>
  <circle cx="{ex}" cy="{ey}" r="1.6" fill="#c9cdd3" stroke="#6d737c" stroke-width="0.25"/>'''
cab = cabeca(l, a)
gravar_svg('servo-360', f'{cab}{corpo}\n{movel}\n</svg>\n')
gravar_svg('servo-360-corpo', f'{cab}{corpo}\n</svg>\n')
gravar_svg('servo-360-movel', f'{cab}{movel}\n</svg>\n')
print(f'  360: eixo em fracao ({ex / l:.5f}, {ey / a:.5f})')
desc360 = dict(desc)
desc360['PWM'] = 'Fio laranja: o sinal. 1500 us para; 1000 us gira de um lado no maximo, 2000 us do outro'
guardar('servo-360', l, a, [(n, 1.5, y, ['power'] if n != 'PWM' else [], desc360[n]) for n, y in zip(['GND', 'V+', 'PWM'], ys)])


cam = BASE + 'app/assets/pecas.json'
d = json.load(io.open(cam, encoding='utf-8'))
novos = {p['id'] for p in pecas}
d = [p for p in d if p['id'] not in novos] + pecas
d.sort(key=lambda p: p['id'])
io.open(cam, 'w', encoding='utf-8', newline='').write(json.dumps(d, ensure_ascii=False, indent=1))
print(f'\ncatalogo: {len(d)} pecas')
