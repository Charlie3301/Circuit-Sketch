"""python ferramentas/extrair-svg/pecas-passo.py

Os drivers de motor de passo da Alpha 23, e o motor que faltava:

  - a4988          : o driver da Pololu (e clones), duas fileiras de 8 pinos
                     0,5" uma da outra (cruza o canal da protoboard), o
                     trimpot do Vref e o dissipador pequeno;
  - drv8825        : o mesmo formato, roxo, com o FAULT e o micropasso 1/32;
  - uln2003        : a placa verde do ULN2003, com IN1..IN4, a alimentacao,
                     o conector de 5 fios do motor e os quatro LEDs A..D;
  - motor-28byj48  : o 28BYJ-48 de 5 V (unipolar, 5 fios, reducao 1:64), visto
                     de cima. Gira o eixo de saida, com um ponteiro branco preso
                     nele -- o eixo anda devagar (15 rpm no maximo).

O motor de passo do Wokwi ("stepper-motor") continua o mesmo desenho: e o
NEMA 17 que estes drivers acionam.
"""

# Aposentado na Alpha 25: os desenhos foram refeitos das referencias de
# verdade em pecas-drivers-a25.py e pecas-motores-a25.py. O codigo fica como historico; rodar este
# arquivo sobrescreveria os desenhos novos.
raise SystemExit('aposentado: use ferramentas/extrair-svg/pecas-drivers-a25.py')
import io
import json
import math

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


def svg(larg, alt, corpo):
    return (f'<svg xmlns="http://www.w3.org/2000/svg" version="1.1"\n'
            f'     width="{larg}mm" height="{alt}mm" viewBox="0 0 {larg} {alt}">\n{corpo}\n</svg>\n')


def texto(x, y, t, tam=1.0, cor='#f2f2f2', ancora='middle', girar=0):
    rot = f' transform="rotate({girar} {x:.2f} {y:.2f})"' if girar else ''
    return (f'  <text x="{x:.2f}" y="{y:.2f}" fill="{cor}" font-size="{tam}px" text-anchor="{ancora}"\n'
            f'        font-family="Verdana" font-weight="bold"{rot}>{t}</text>')


def pino_macho(x, y):
    return (f'  <rect x="{x - 1.2:.2f}" y="{y - 1.2:.2f}" width="2.4" height="2.4" fill="#1b1b1d"/>\n'
            f'  <rect x="{x - 0.38:.2f}" y="{y - 0.38:.2f}" width="0.76" height="0.76" fill="#d9b44a"/>')


# =====================================================================
#  A4988 e DRV8825 (o formato da Pololu)
# =====================================================================
def driver_de_passo(id_peca, cima, baixo, cor, chip, descricoes, dissipador):
    l = 8 * PASSO
    a = 1.27 + 12.7 + 1.27
    y_cima, y_baixo = 1.27, 1.27 + 12.7
    corpo = f'  <rect x="0.15" y="0.15" width="{l - 0.3:.2f}" height="{a - 0.3:.2f}" rx="0.7" fill="{cor}" stroke="#00000055" stroke-width="0.3"/>\n'
    if dissipador:
        corpo += (f'  <rect x="{l / 2 - 4.6:.2f}" y="{a / 2 - 3.6:.2f}" width="9.2" height="7.2" rx="0.4" fill="#c7ccd3" stroke="#7d838c" stroke-width="0.25"/>\n'
                  + ''.join(f'  <path d="M{l / 2 - 3.8 + i * 1.5:.2f} {a / 2 - 3.2:.2f} V{a / 2 + 3.2:.2f}" stroke="#9aa0a8" stroke-width="0.6"/>\n' for i in range(6)))
    else:
        corpo += f'  <rect x="{l / 2 - 3.2:.2f}" y="{a / 2 - 2.6:.2f}" width="6.4" height="5.2" rx="0.3" fill="#1b1b1d"/>\n'
        corpo += texto(l / 2, a / 2 + 0.4, chip, 0.8, '#bbbbbb') + '\n'
    # o trimpot do Vref, no canto
    corpo += (f'  <circle cx="{l - 3.6:.2f}" cy="{a / 2:.2f}" r="1.25" fill="#d8d8d2" stroke="#8f8f8a" stroke-width="0.2"/>\n'
              f'  <path d="M{l - 4.4:.2f} {a / 2:.2f} H{l - 2.8:.2f}" stroke="#5a5a55" stroke-width="0.35"/>\n')
    for i, (nc, nb) in enumerate(zip(cima, baixo)):
        x = 1.27 + i * PASSO
        corpo += pino_macho(x, y_cima) + '\n' + pino_macho(x, y_baixo) + '\n'
        corpo += texto(x, y_cima + 2.7, nc.split('.')[0], 0.62, girar=-90 if len(nc.split('.')[0]) > 2 else 0) + '\n'
        corpo += texto(x, y_baixo - 1.9, nb, 0.62, girar=-90 if len(nb) > 2 else 0) + '\n'
    gravar_svg(id_peca, svg(round(l, 2), round(a, 2), corpo))
    pinos = [(n, 1.27 + i * PASSO, y_cima, ['power'] if n.split('.')[0] in ('VMOT', 'GND', 'VDD') else [], descricoes[n])
             for i, n in enumerate(cima)]
    pinos += [(n, 1.27 + i * PASSO, y_baixo, [], descricoes[n]) for i, n in enumerate(baixo)]
    guardar(id_peca, round(l, 2), round(a, 2), pinos)


comuns = {
    'VMOT': 'Alimentacao do motor: o + da fonte (8 a 35 V). Com 5 V ele nao liga',
    'GND': 'GND do motor: o - da fonte (e o GND da placa)', 'GND.2': 'GND da logica: o GND da placa',
    'EN': 'Enable, ativo em LOW: solto ou LOW = ligado; HIGH desliga as bobinas (o eixo fica solto)',
    'RST': 'Reset, ativo em LOW. Solto ele fica em reset e o motor nao anda: ligue no SLP (o jeito da Pololu)',
    'SLP': 'Sleep, ativo em LOW. A placa ja puxa para HIGH (acordado)',
    'STEP': 'Cada borda de subida anda um (micro)passo',
    'DIR': 'O sentido: HIGH para um lado, LOW para o outro',
}
driver_de_passo(
    'a4988',
    ['VMOT', 'GND', '2B', '2A', '1A', '1B', 'VDD', 'GND.2'],
    ['EN', 'MS1', 'MS2', 'MS3', 'RST', 'SLP', 'STEP', 'DIR'],
    '#1d6b3a', 'A4988', comuns | {
        '2B': 'Bobina 2 do motor (com o 2A)', '2A': 'Bobina 2 do motor (com o 2B)',
        '1A': 'Bobina 1 do motor (com o 1B)', '1B': 'Bobina 1 do motor (com o 1A)',
        'VDD': 'Alimentacao da logica: 3,3 V ou 5 V da placa',
        'MS1': 'Micropasso: MS1 MS2 MS3 = LLL passo cheio, HLL 1/2, LHL 1/4, HHL 1/8, HHH 1/16 (solto = L)',
        'MS2': 'Micropasso (ver MS1)', 'MS3': 'Micropasso (ver MS1)',
    }, dissipador=True)

driver_de_passo(
    'drv8825',
    ['VMOT', 'GND', 'B2', 'B1', 'A1', 'A2', 'FLT', 'GND.2'],
    ['EN', 'M0', 'M1', 'M2', 'RST', 'SLP', 'STEP', 'DIR'],
    '#4b2a8c', 'DRV8825', comuns | {
        'VMOT': 'Alimentacao do motor: o + da fonte (8,2 a 45 V). Com 5 V ele nao liga',
        'B2': 'Bobina B do motor (com o B1)', 'B1': 'Bobina B do motor (com o B2)',
        'A1': 'Bobina A do motor (com o A2)', 'A2': 'Bobina A do motor (com o A1)',
        'FLT': 'Fault: vai a LOW quando o driver se protege',
        'M0': 'Micropasso: M0 M1 M2 = 000 cheio, 100 1/2, 010 1/4, 110 1/8, 001 1/16, 101 ou mais 1/32 (solto = 0)',
        'M1': 'Micropasso (ver M0)', 'M2': 'Micropasso (ver M0)',
    }, dissipador=False)


# =====================================================================
#  ULN2003 (a placa verde do 28BYJ-48)
# =====================================================================
l, a = 35.0, 32.0
ys_in = [9.0 + i * PASSO for i in range(4)]
xs_pot = [24.92, 27.46]
y_pot = 30.2
nomes_motor = ['D', 'C', 'B', 'A', 'COM']
xs_motor = [11.42 + i * PASSO for i in range(5)]
y_motor = 2.0
corpo = f'''  <rect x="0.3" y="0.3" width="{l - 0.6}" height="{a - 0.6}" rx="1.0" fill="#1f7a3e" stroke="#0f3d1f" stroke-width="0.3"/>
  <circle cx="2.4" cy="2.4" r="1.2" fill="#0e2a16"/><circle cx="{l - 2.4}" cy="{a - 2.4}" r="1.2" fill="#0e2a16"/>
  <!-- o conector branco do motor (JST de 5 vias) -->
  <rect x="{xs_motor[0] - 1.8:.2f}" y="0.6" width="{4 * PASSO + 3.6:.2f}" height="3.4" rx="0.3" fill="#f1efe6" stroke="#b9b6aa" stroke-width="0.25"/>
  <!-- o ULN2003 (DIP-16) -->
  <rect x="9.0" y="12.0" width="10.5" height="12.5" rx="0.4" fill="#1b1b1d"/>
{texto(14.25, 18.8, "ULN2003", 1.0, "#bbbbbb", girar=-90)}
'''
for i, n in enumerate(nomes_motor):
    corpo += f'  <rect x="{xs_motor[i] - 0.4:.2f}" y="{y_motor - 0.4:.2f}" width="0.8" height="0.8" fill="#d9b44a"/>\n'
for i, y in enumerate(ys_in):
    corpo += pino_macho(1.8, y) + '\n' + texto(3.6, y + 0.35, f'IN{i + 1}', 0.9, ancora='start') + '\n'
for x, n in zip(xs_pot, ['-', '+']):
    corpo += pino_macho(x, y_pot) + '\n' + texto(x, y_pot - 1.9, n, 1.3) + '\n'
corpo += texto(26.2, y_pot - 3.6, '5-12V', 0.85) + '\n'
for i, letra in enumerate('ABCD'):
    y = 12.5 + i * 3.0
    corpo += (f'  <circle cx="26.0" cy="{y:.2f}" r="0.9" fill="#5a1414" stroke="#2a0b0b" stroke-width="0.2"/>\n'
              + texto(28.4, y + 0.45, letra, 1.1, ancora='start') + '\n')
gravar_svg('uln2003', svg(l, a, corpo))

desc_uln = {
    'IN1': 'Entrada 1: HIGH liga a fase A (fio laranja do motor) e o LED A',
    'IN2': 'Entrada 2: fase B (fio amarelo)', 'IN3': 'Entrada 3: fase C (fio rosa)', 'IN4': 'Entrada 4: fase D (fio azul)',
    '-': 'GND: o GND da placa (e o - da fonte)', '+': 'Alimentacao do motor, 5 a 12 V (o 28BYJ-48 e de 5 V)',
    'A': 'Conector do motor: fio laranja', 'B': 'Conector do motor: fio amarelo',
    'C': 'Conector do motor: fio rosa', 'D': 'Conector do motor: fio azul',
    'COM': 'Conector do motor: fio vermelho (o comum, ligado no + da placa)',
}
guardar('uln2003', l, a,
        [(f'IN{i + 1}', 1.8, y, [], desc_uln[f'IN{i + 1}']) for i, y in enumerate(ys_in)]
        + [(n, x, y_pot, ['power'], desc_uln[n]) for n, x in zip(['-', '+'], xs_pot)]
        + [(n, x, y_motor, [], desc_uln[n]) for n, x in zip(nomes_motor, xs_motor)])


# =====================================================================
#  28BYJ-48 (de cima)
# =====================================================================
l, a = 44.0, 46.0
cx, cy = 22.0, 20.0            # o centro da lata
ex, ey = cx, cy - 8.0          # o eixo de saida, 8 mm fora do centro
cores = [('azul', '#2d6cdf'), ('rosa', '#ef7fb4'), ('amarelo', '#f2c230'), ('laranja', '#f08a24'), ('vermelho', '#d23b33')]
xs_fio = [cx - 2 * PASSO + i * PASSO for i in range(5)]
y_fio = a - 1.3

corpo = ''
# os cinco fios, da lata ate o conector embaixo
for (nome, cor), x in zip(cores, xs_fio):
    corpo += (f'  <path d="M{cx - 2.0 + (x - cx) * 0.4:.2f} {cy + 13.0:.2f} C{cx - 2.0 + (x - cx) * 0.4:.2f} {cy + 18:.2f} {x:.2f} {cy + 17:.2f} {x:.2f} {y_fio - 2.0:.2f}" '
              f'fill="none" stroke="#00000066" stroke-width="1.0"/>\n'
              f'  <path d="M{cx - 2.0 + (x - cx) * 0.4:.2f} {cy + 13.0:.2f} C{cx - 2.0 + (x - cx) * 0.4:.2f} {cy + 18:.2f} {x:.2f} {cy + 17:.2f} {x:.2f} {y_fio - 2.0:.2f}" '
              f'fill="none" stroke="{cor}" stroke-width="0.7"/>\n')
corpo += f'  <rect x="{xs_fio[0] - 1.8:.2f}" y="{y_fio - 2.6:.2f}" width="{4 * PASSO + 3.6:.2f}" height="3.6" rx="0.3" fill="#f1efe6" stroke="#b9b6aa" stroke-width="0.25"/>\n'
for x in xs_fio:
    corpo += f'  <rect x="{x - 0.4:.2f}" y="{y_fio - 0.4:.2f}" width="0.8" height="0.8" fill="#d9b44a"/>\n'
# as orelhas de fixacao e a lata
corpo += f'''  <path d="M{cx - 21.0:.2f} {cy:.2f} A3.5 3.5 0 0 1 {cx - 17.5:.2f} {cy - 3.5:.2f} H{cx + 17.5:.2f} A3.5 3.5 0 0 1 {cx + 21.0:.2f} {cy:.2f} A3.5 3.5 0 0 1 {cx + 17.5:.2f} {cy + 3.5:.2f} H{cx - 17.5:.2f} A3.5 3.5 0 0 1 {cx - 21.0:.2f} {cy:.2f} Z" fill="#b9bec6" stroke="#6d737c" stroke-width="0.3"/>
  <circle cx="{cx - 17.5:.2f}" cy="{cy:.2f}" r="2.1" fill="#2a2a2e"/>
  <circle cx="{cx + 17.5:.2f}" cy="{cy:.2f}" r="2.1" fill="#2a2a2e"/>
  <circle cx="{cx:.2f}" cy="{cy:.2f}" r="14.0" fill="#cfd3d9" stroke="#7d838c" stroke-width="0.35"/>
  <circle cx="{cx:.2f}" cy="{cy:.2f}" r="12.6" fill="#1f5fb4" stroke="#123e7a" stroke-width="0.3"/>
  <!-- o ressalto do eixo -->
  <circle cx="{ex:.2f}" cy="{ey:.2f}" r="4.6" fill="#e3e6ea" stroke="#7d838c" stroke-width="0.3"/>
{texto(cx, cy + 6.5, "28BYJ-48", 1.6, "#dfe9ff")}
{texto(cx, cy + 8.6, "5V DC", 1.2, "#dfe9ff")}
'''
movel = f'''  <!-- o ponteiro branco preso no eixo (aponta para cima parado) -->
  <path d="M{ex - 1.0:.2f} {ey + 1.6:.2f} L{ex - 0.55:.2f} {ey - 9.5:.2f} L{ex:.2f} {ey - 10.6:.2f} L{ex + 0.55:.2f} {ey - 9.5:.2f} L{ex + 1.0:.2f} {ey + 1.6:.2f} Z" fill="#f7f7f4" stroke="#8f8f8a" stroke-width="0.2"/>
  <path d="M{ex - 1.5:.2f} {ey - 0.9:.2f} H{ex + 1.5:.2f} V{ey + 0.9:.2f} H{ex - 1.5:.2f} Z" fill="#c9cdd3" stroke="#5a6068" stroke-width="0.15"/>
  <circle cx="{ex:.2f}" cy="{ey - 9.4:.2f}" r="0.45" fill="#d23b33"/>'''
cab = (f'<svg xmlns="http://www.w3.org/2000/svg" version="1.1"\n'
       f'     width="{l}mm" height="{a}mm" viewBox="0 0 {l} {a}">\n')
gravar_svg('motor-28byj48', f'{cab}{corpo}\n{movel}\n</svg>\n')
gravar_svg('motor-28byj48-corpo', f'{cab}{corpo}\n</svg>\n')
gravar_svg('motor-28byj48-movel', f'{cab}{movel}\n</svg>\n')
print(f'  28BYJ-48: eixo em ({ex} mm, {ey} mm) = fracao ({ex / l:.5f}, {ey / a:.5f})')

desc_motor = {
    'azul': 'Fio azul: fase D (o IN4 do ULN2003)', 'rosa': 'Fio rosa: fase C (o IN3)',
    'amarelo': 'Fio amarelo: fase B (o IN2)', 'laranja': 'Fio laranja: fase A (o IN1)',
    'vermelho': 'Fio vermelho: o comum das bobinas, no + (5 V)',
}
guardar('motor-28byj48', l, a, [(n, x, y_fio, ['power'] if n == 'vermelho' else [], desc_motor[n])
                                 for (n, _), x in zip(cores, xs_fio)])


cam = BASE + 'app/assets/pecas.json'
d = json.load(io.open(cam, encoding='utf-8'))
novos = {p['id'] for p in pecas}
d = [p for p in d if p['id'] not in novos] + pecas
d.sort(key=lambda p: p['id'])
io.open(cam, 'w', encoding='utf-8', newline='').write(json.dumps(d, ensure_ascii=False, indent=1))
print(f'\ncatalogo: {len(d)} pecas')
