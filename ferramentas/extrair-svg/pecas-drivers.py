"""python ferramentas/extrair-svg/pecas-drivers.py

As pontes H e os drivers de motor DC da Alpha 23:

  - l298n      : o modulo vermelho do L298N, com dissipador, bornes de
                 OUT1/OUT2 (esquerda) e OUT3/OUT4 (direita), o borne de
                 alimentacao (12V, GND, 5V) e o conector ENA..ENB embaixo;
  - l293d      : o CI de 16 pinos (DIP), na pinagem da TI;
  - l9110s     : o modulo pequeno de dois L9110S, bornes MOTOR A/B em cima e
                 o conector de 6 pinos embaixo;
  - tb6612fng  : a placa da SparkFun, duas fileiras de 8 pinos 0,6" uma da
                 outra (cruza o canal da protoboard);
  - drv8833    : a placa do DRV8833, duas fileiras de 6;
  - bts7960    : o modulo IBT-2 (43 A), dissipador grande, conector de 8 pinos
                 e os quatro bornes de potencia.

Os bornes tem passo de 5,08 mm e os conectores de 2,54: tudo no passo de
0,1", como o autoteste confere. O que acende (a luz de energia dos modulos)
o editor desenha por cima; aqui fica so o desenho parado.
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


def svg(larg, alt, corpo):
    return (f'<svg xmlns="http://www.w3.org/2000/svg" version="1.1"\n'
            f'     width="{larg}mm" height="{alt}mm" viewBox="0 0 {larg} {alt}">\n{corpo}\n</svg>\n')


def texto(x, y, t, tam=1.3, cor='#f2f2f2', ancora='middle', peso='bold', girar=0):
    rot = f' transform="rotate({girar} {x:.2f} {y:.2f})"' if girar else ''
    return (f'  <text x="{x:.2f}" y="{y:.2f}" fill="{cor}" font-size="{tam}px" text-anchor="{ancora}"\n'
            f'        font-family="Verdana" font-weight="{peso}"{rot}>{t}</text>')


def pino_macho(x, y):
    """Um pino de barra de pinos visto de cima: a base preta e o pino dourado."""
    return (f'  <rect x="{x - 1.2:.2f}" y="{y - 1.2:.2f}" width="2.4" height="2.4" fill="#1b1b1d"/>\n'
            f'  <rect x="{x - 0.38:.2f}" y="{y - 0.38:.2f}" width="0.76" height="0.76" fill="#d9b44a"/>')


def borne(x0, y0, n, vertical=False, cor='#1f5fb4', passo=5.08):
    """Um borne de parafuso com n vias: o bloco e os parafusos (o pino e o parafuso)."""
    if vertical:
        larg, alt = 6.0, n * passo + 0.6
        corpo = f'  <rect x="{x0:.2f}" y="{y0:.2f}" width="{larg}" height="{alt:.2f}" rx="0.5" fill="{cor}" stroke="#0f2f5c" stroke-width="0.25"/>\n'
        centros = [(x0 + larg / 2, y0 + 0.3 + passo / 2 + i * passo) for i in range(n)]
    else:
        larg, alt = n * passo + 0.6, 6.0
        corpo = f'  <rect x="{x0:.2f}" y="{y0:.2f}" width="{larg:.2f}" height="{alt}" rx="0.5" fill="{cor}" stroke="#0f2f5c" stroke-width="0.25"/>\n'
        centros = [(x0 + 0.3 + passo / 2 + i * passo, y0 + alt / 2) for i in range(n)]
    for cx, cy in centros:
        corpo += (f'  <circle cx="{cx:.2f}" cy="{cy:.2f}" r="1.75" fill="#c9cdd3" stroke="#6d737c" stroke-width="0.25"/>\n'
                  f'  <path d="M{cx - 1.1:.2f} {cy:.2f} H{cx + 1.1:.2f}" stroke="#5a6068" stroke-width="0.45"/>\n')
    return corpo, centros


# =====================================================================
#  L298N (modulo)
# =====================================================================
l, a = 43.0, 43.0
y_baixo = 41.5
xs_baixo = [7.0 + i * PASSO for i in range(9)]   # 12V GND 5V | ENA IN1 IN2 IN3 IN4 ENB
nomes_baixo = ['12V', 'GND', '5V', 'ENA', 'IN1', 'IN2', 'IN3', 'IN4', 'ENB']
xs_baixo = [7.0, 12.08, 17.16, 22.24, 24.78, 27.32, 29.86, 32.40, 34.94]
y_out = [15.7, 20.78]

corpo = f'''  <rect x="0.3" y="0.3" width="{l - 0.6}" height="{a - 0.6}" rx="1.4" fill="#b3202a" stroke="#7d151c" stroke-width="0.3"/>
  <circle cx="2.6" cy="2.6" r="1.3" fill="#2a1013"/><circle cx="{l - 2.6}" cy="2.6" r="1.3" fill="#2a1013"/>
  <circle cx="2.6" cy="{a - 2.6}" r="1.3" fill="#2a1013"/><circle cx="{l - 2.6}" cy="{a - 2.6}" r="1.3" fill="#2a1013"/>
  <!-- o dissipador e o L298N (Multiwatt) na frente dele -->
  <rect x="10.5" y="1.2" width="22" height="15.5" rx="0.6" fill="#1e1f22" stroke="#0b0b0c" stroke-width="0.3"/>
''' + ''.join(f'  <path d="M{12.0 + i * 1.9:.2f} 2.2 V15.6" stroke="#3a3c41" stroke-width="0.9"/>\n' for i in range(11)) + f'''  <rect x="13.2" y="15.2" width="16.6" height="5.2" rx="0.4" fill="#141416" stroke="#000" stroke-width="0.2"/>
{texto(21.5, 18.6, "L298N", 1.8, "#d9d9d9")}
  <!-- os diodos de roda livre, o 78M05, os capacitores -->
''' + ''.join(f'  <rect x="{12.0 + i * 2.5:.2f}" y="23.2" width="1.4" height="3.4" rx="0.3" fill="#1b1b1d"/>\n  <rect x="{12.0 + i * 2.5:.2f}" y="25.6" width="1.4" height="0.6" fill="#c9cdd3"/>\n' for i in range(8)) + f'''  <rect x="30.5" y="28.0" width="5.0" height="4.2" rx="0.3" fill="#1b1b1d"/>
{texto(33.0, 30.7, "78M05", 0.9, "#bbbbbb")}
  <circle cx="7.2" cy="30.5" r="2.6" fill="#1b2a5c" stroke="#0e1532" stroke-width="0.3"/>
  <circle cx="38.6" cy="30.5" r="2.6" fill="#1b2a5c" stroke="#0e1532" stroke-width="0.3"/>
  <!-- a luz de energia (apagada; o editor acende) -->
  <circle cx="24.0" cy="31.0" r="0.9" fill="#5a1414" stroke="#2a0b0b" stroke-width="0.2"/>
{texto(24.0, 34.0, "PWR", 0.9, "#f0c9c9")}
'''
b, c_esq = borne(0.4, 12.6, 2, vertical=True)
corpo += b
b, c_dir = borne(36.6, 12.6, 2, vertical=True)
corpo += b
b, c_pot = borne(4.16, 35.0, 3)
corpo += b
corpo += texto(5.0, 11.4, 'OUT1', 1.1, ancora='start') + '\n'
corpo += texto(5.0, 27.0, 'OUT2', 1.1, ancora='start') + '\n'
corpo += texto(38.0, 11.4, 'OUT3', 1.1, ancora='end') + '\n'
corpo += texto(38.0, 27.0, 'OUT4', 1.1, ancora='end') + '\n'
for n, x in zip(nomes_baixo, xs_baixo):
    if n not in ('12V', 'GND', '5V'):
        corpo += pino_macho(x, y_baixo) + '\n'
    corpo += texto(x, 38.9 if n not in ('12V', 'GND', '5V') else 34.3, n, 0.95) + '\n'
gravar_svg('l298n', svg(l, a, corpo))

desc_l298n = {
    '12V': 'Alimentacao dos motores, 5 a 35 V (o + da bateria). Com 7 V ou mais o regulador do modulo faz os 5 V da logica',
    'GND': 'GND: o - da bateria E o GND da placa, os dois aqui (sem GND em comum o L298N nao entende os sinais)',
    '5V': 'Com 7 V ou mais no 12V, e SAIDA de 5 V (da para alimentar a placa). Abaixo disso, ligue aqui os 5 V da placa',
    'ENA': 'Liga o canal A (OUT1/OUT2). Solto = o jumper no lugar (sempre ligado); num pino com PWM, controla a velocidade',
    'IN1': 'Sentido do canal A: IN1 HIGH e IN2 LOW gira para um lado, o contrario para o outro, iguais freiam',
    'IN2': 'Sentido do canal A (par do IN1)',
    'IN3': 'Sentido do canal B: IN3 HIGH e IN4 LOW gira para um lado, o contrario para o outro, iguais freiam',
    'IN4': 'Sentido do canal B (par do IN3)',
    'ENB': 'Liga o canal B (OUT3/OUT4). Solto = o jumper no lugar (sempre ligado); num pino com PWM, controla a velocidade',
    'OUT1': 'Saida do canal A: um fio do motor A', 'OUT2': 'Saida do canal A: o outro fio do motor A',
    'OUT3': 'Saida do canal B: um fio do motor B', 'OUT4': 'Saida do canal B: o outro fio do motor B',
}
guardar('l298n', l, a,
        [('OUT1', c_esq[0][0], c_esq[0][1], [], desc_l298n['OUT1']), ('OUT2', c_esq[1][0], c_esq[1][1], [], desc_l298n['OUT2']),
         ('OUT3', c_dir[0][0], c_dir[0][1], [], desc_l298n['OUT3']), ('OUT4', c_dir[1][0], c_dir[1][1], [], desc_l298n['OUT4'])]
        + [(n, cx, cy, ['power'], desc_l298n[n]) for n, (cx, cy) in zip(['12V', 'GND', '5V'], c_pot)]
        + [(n, x, y_baixo, [], desc_l298n[n]) for n, x in zip(nomes_baixo[3:], xs_baixo[3:])])


# =====================================================================
#  L293D (DIP-16)
# =====================================================================
# 81 x 36 px exatos: a proporcao do desenho tem que bater com a da caixa
l, a = 81 / PX, 36 / PX
y_cima = (a - 7.62) / 2
y_baixo = y_cima + 7.62
nomes_l293d = ['1,2EN', '1A', '1Y', 'GND.1', 'GND.2', '2Y', '2A', 'VCC2',
               '3,4EN', '3A', '3Y', 'GND.3', 'GND.4', '4Y', '4A', 'VCC1']
corpo = ''
for i in range(8):
    x = 1.5 + i * PASSO
    corpo += f'  <rect x="{x - 0.45:.2f}" y="{y_cima - 0.6:.2f}" width="0.9" height="1.9" fill="#c9cdd3"/>\n'
    corpo += f'  <rect x="{x - 0.45:.2f}" y="{y_baixo - 1.3:.2f}" width="0.9" height="1.9" fill="#c9cdd3"/>\n'
corpo += f'''  <rect x="0.4" y="2.0" width="{l - 0.8:.2f}" height="5.6" rx="0.4" fill="#1d1d20" stroke="#000" stroke-width="0.2"/>
  <path d="M0.4 3.9 A0.9 0.9 0 0 1 0.4 5.7" fill="#0c0c0d"/>
  <circle cx="1.6" cy="6.6" r="0.35" fill="#55575c"/>
{texto(l / 2, 5.3, "L293D", 1.6, "#d4d4d4")}
'''
gravar_svg('l293d', svg(l, a, corpo))

desc_l293d = {
    '1,2EN': 'Pino 1: liga as saidas 1Y e 2Y (HIGH ou PWM). Em LOW (ou solto) elas desligam e o motor roda livre',
    '1A': 'Pino 2: entrada da saida 1Y', '1Y': 'Pino 3: saida 1 (um fio do motor 1)',
    'GND.1': 'Pino 4: GND (e dissipador). Os quatro GND sao o mesmo', 'GND.2': 'Pino 5: GND',
    '2Y': 'Pino 6: saida 2 (o outro fio do motor 1)', '2A': 'Pino 7: entrada da saida 2Y',
    'VCC2': 'Pino 8: alimentacao dos motores, 4,5 a 36 V (o + da bateria)',
    '3,4EN': 'Pino 9: liga as saidas 3Y e 4Y (HIGH ou PWM)', '3A': 'Pino 10: entrada da saida 3Y',
    '3Y': 'Pino 11: saida 3 (um fio do motor 2)', 'GND.3': 'Pino 12: GND', 'GND.4': 'Pino 13: GND',
    '4Y': 'Pino 14: saida 4 (o outro fio do motor 2)', '4A': 'Pino 15: entrada da saida 4Y',
    'VCC1': 'Pino 16: alimentacao da logica, 5 V (o 5V da placa)',
}
pinos = []
for i in range(8):
    n = nomes_l293d[i]                     # pinos 1..8 embaixo, da esquerda para a direita
    pinos.append((n, 1.5 + i * PASSO, y_baixo, ['power'] if n.startswith(('GND', 'VCC')) else [], desc_l293d[n]))
for i in range(8):
    n = nomes_l293d[15 - i]                # pinos 16..9 em cima, da esquerda para a direita
    pinos.append((n, 1.5 + i * PASSO, y_cima, ['power'] if n.startswith(('GND', 'VCC')) else [], desc_l293d[n]))
guardar('l293d', l, a, pinos)


# =====================================================================
#  L9110S (modulo de dois canais)
# =====================================================================
l, a = 29.0, 22.0
y_motor, y_conector = 3.4, 20.6
xs_motor = [3.42, 8.50, 18.66, 23.74]
nomes_motor = ['A1', 'A2', 'B1', 'B2']
x0_con = (l - 5 * PASSO) / 2
nomes_con = ['B-IA', 'B-IB', 'GND', 'VCC', 'A-IA', 'A-IB']
xs_con = [x0_con + i * PASSO for i in range(6)]
corpo = f'''  <rect x="0.3" y="0.3" width="{l - 0.6}" height="{a - 0.6}" rx="1.0" fill="#14457a" stroke="#0b2948" stroke-width="0.3"/>
'''
b, c_a = borne(0.39, 0.4, 2, cor='#2b8a3e')
corpo += b
b, c_b = borne(15.63, 0.4, 2, cor='#2b8a3e')
corpo += b
corpo += f'''  <rect x="6.0" y="9.4" width="5.0" height="4.0" rx="0.3" fill="#1b1b1d"/>
  <rect x="17.5" y="9.4" width="5.0" height="4.0" rx="0.3" fill="#1b1b1d"/>
{texto(8.5, 12.0, "9110S", 0.9, "#bbbbbb")}
{texto(20.0, 12.0, "9110S", 0.9, "#bbbbbb")}
{texto(5.96, 8.4, "MOTOR A", 1.0)}
{texto(21.2, 8.4, "MOTOR B", 1.0)}
  <circle cx="{l - 3.0}" cy="11.4" r="0.8" fill="#5a1414" stroke="#2a0b0b" stroke-width="0.2"/>
'''
for n, x in zip(nomes_con, xs_con):
    corpo += pino_macho(x, y_conector) + '\n'
    corpo += texto(x, 17.6, n, 0.75, girar=-60) + '\n'
gravar_svg('l9110s', svg(l, a, corpo))

desc_l9110s = {
    'A1': 'MOTOR A: um fio do motor', 'A2': 'MOTOR A: o outro fio',
    'B1': 'MOTOR B: um fio do motor', 'B2': 'MOTOR B: o outro fio',
    'B-IA': 'Entrada do motor B (par do B-IB): as saidas seguem as entradas',
    'B-IB': 'Entrada do motor B: B-IA HIGH e B-IB LOW gira para um lado; iguais freiam',
    'GND': 'GND: o - da bateria e o GND da placa, juntos',
    'VCC': 'Alimentacao, 2,5 a 12 V: e a dos motores E a da logica (o + da bateria)',
    'A-IA': 'Entrada do motor A: num pino com PWM, a velocidade', 'A-IB': 'Entrada do motor A (par do A-IA)',
}
guardar('l9110s', l, a,
        [(n, cx, cy, [], desc_l9110s[n]) for n, (cx, cy) in zip(nomes_motor, c_a + c_b)]
        + [(n, x, y_conector, ['power'] if n in ('GND', 'VCC') else [], desc_l9110s[n]) for n, x in zip(nomes_con, xs_con)])


# =====================================================================
#  TB6612FNG (placa da SparkFun) e DRV8833
# =====================================================================
def placa_de_duas_fileiras(id_peca, cima, baixo, cor, rotulo, chip, descricoes):
    n = len(cima)
    l = n * PASSO
    a = 1.27 + 15.24 + 1.27
    y_cima, y_baixo = 1.27, 1.27 + 15.24
    corpo = f'''  <rect x="0.15" y="0.15" width="{l - 0.3:.2f}" height="{a - 0.3:.2f}" rx="0.8" fill="{cor}" stroke="#00000055" stroke-width="0.3"/>
  <rect x="{l / 2 - 3.2:.2f}" y="{a / 2 - 2.6:.2f}" width="6.4" height="5.2" rx="0.3" fill="#1b1b1d"/>
{texto(l / 2, a / 2 + 0.4, chip, 0.85, "#bbbbbb")}
'''
    for i, (nc, nb) in enumerate(zip(cima, baixo)):
        x = 1.27 + i * PASSO
        corpo += pino_macho(x, y_cima) + '\n' + pino_macho(x, y_baixo) + '\n'
        corpo += texto(x, y_cima + 3.4, nc.split('.')[0], 0.72, girar=-90 if len(nc) > 3 else 0) + '\n'
        corpo += texto(x, y_baixo - 2.2, nb.split('.')[0], 0.72, girar=-90 if len(nb) > 3 else 0) + '\n'
    gravar_svg(id_peca, svg(l, round(a, 2), corpo))
    pinos = [(nc, 1.27 + i * PASSO, y_cima, ['power'] if nc.split('.')[0] in ('VM', 'VCC', 'GND') else [], descricoes[nc])
             for i, nc in enumerate(cima)]
    pinos += [(nb, 1.27 + i * PASSO, y_baixo, ['power'] if nb.split('.')[0] in ('VM', 'VCC', 'GND') else [], descricoes[nb])
              for i, nb in enumerate(baixo)]
    guardar(id_peca, l, round(a, 2), pinos)


placa_de_duas_fileiras(
    'tb6612fng',
    ['VM', 'VCC', 'GND.2', 'AO1', 'AO2', 'BO2', 'BO1', 'GND.3'],
    ['PWMA', 'AIN2', 'AIN1', 'STBY', 'BIN1', 'BIN2', 'PWMB', 'GND'],
    '#c0272d', 'TB6612FNG', 'TB6612', {
        'VM': 'Alimentacao dos motores, 2,5 a 13,5 V (o + da bateria)',
        'VCC': 'Alimentacao da logica, 2,7 a 5,5 V (o 3V3 ou o 5V da placa)',
        'GND.2': 'GND (os tres GND sao o mesmo): o - da bateria e o GND da placa',
        'AO1': 'Saida A: um fio do motor A', 'AO2': 'Saida A: o outro fio do motor A',
        'BO2': 'Saida B: o outro fio do motor B', 'BO1': 'Saida B: um fio do motor B',
        'GND.3': 'GND', 'GND': 'GND',
        'PWMA': 'Velocidade do motor A (PWM). Em LOW, freio curto',
        'AIN2': 'Sentido do motor A (par do AIN1)',
        'AIN1': 'Sentido do motor A: AIN1 HIGH e AIN2 LOW gira para um lado; os dois LOW = roda livre; os dois HIGH = freio',
        'STBY': 'Standby: precisa de HIGH para funcionar (solto ou em LOW, tudo desliga)',
        'BIN1': 'Sentido do motor B: BIN1 HIGH e BIN2 LOW gira para um lado',
        'BIN2': 'Sentido do motor B (par do BIN1)',
        'PWMB': 'Velocidade do motor B (PWM). Em LOW, freio curto',
    })

placa_de_duas_fileiras(
    'drv8833',
    ['AO1', 'AO2', 'BO1', 'BO2', 'VM', 'GND'],
    ['AIN1', 'AIN2', 'BIN1', 'BIN2', 'SLP', 'FLT'],
    '#3b2f8f', 'DRV8833', 'DRV8833', {
        'AO1': 'Saida A: um fio do motor A', 'AO2': 'Saida A: o outro fio do motor A',
        'BO1': 'Saida B: um fio do motor B', 'BO2': 'Saida B: o outro fio do motor B',
        'VM': 'Alimentacao, 2,7 a 10,8 V (motores e logica; o + da bateria)',
        'GND': 'GND: o - da bateria e o GND da placa',
        'AIN1': 'Motor A: AIN1 HIGH e AIN2 LOW gira para um lado; os dois LOW = roda livre; os dois HIGH = freio. PWM aqui = velocidade',
        'AIN2': 'Motor A (par do AIN1)', 'BIN1': 'Motor B: como o AIN1', 'BIN2': 'Motor B (par do BIN1)',
        'SLP': 'Sleep: a placa ja puxa para HIGH (acordado). Em LOW, tudo desliga',
        'FLT': 'Fault: vai a LOW quando o driver se protege (curto ou calor)',
    })


# =====================================================================
#  BTS7960 (modulo IBT-2)
# =====================================================================
l, a = 50.0, 50.0
nomes_ctl = ['RPWM', 'LPWM', 'R_EN', 'L_EN', 'R_IS', 'L_IS', 'VCC', 'GND']
ys_ctl = [15.0 + i * PASSO for i in range(8)]
nomes_pot = ['B+', 'B-', 'M+', 'M-']
ys_pot = [10.0, 15.08, 30.32, 35.40]
corpo = f'''  <rect x="0.3" y="0.3" width="{l - 0.6}" height="{a - 0.6}" rx="1.2" fill="#1d5c3a" stroke="#0d2e1c" stroke-width="0.3"/>
  <!-- o dissipador de aluminio, quase a placa toda -->
  <rect x="8.0" y="3.0" width="31.0" height="44.0" rx="1.0" fill="#b9bec6" stroke="#7d838c" stroke-width="0.35"/>
''' + ''.join(f'  <path d="M{10.0 + i * 2.25:.2f} 4.0 V46.0" stroke="#8e949c" stroke-width="1.0"/>\n' for i in range(13)) + f'''{texto(23.5, 26.0, "BTS7960", 2.2, "#3a3e45")}
{texto(23.5, 29.0, "43 A", 1.6, "#3a3e45")}
'''
b, c_bat = borne(42.6, 7.1, 2, vertical=True, cor='#2b8a3e')
corpo += b
b, c_mot = borne(42.6, 27.42, 2, vertical=True, cor='#2b8a3e')
corpo += b
ys_pot = [cy for _, cy in c_bat + c_mot]
for n, y in zip(nomes_ctl, ys_ctl):
    corpo += pino_macho(1.6, y) + '\n' + texto(3.4, y + 0.4, n, 0.85, ancora='start') + '\n'
for n, y in zip(nomes_pot, ys_pot):
    corpo += texto(41.8, y + 0.4, n, 1.2, ancora='end') + '\n'
gravar_svg('bts7960', svg(l, a, corpo))

desc_bts = {
    'RPWM': 'PWM do lado "R": com R_EN em HIGH, o M+ segue este pino (velocidade para um lado)',
    'LPWM': 'PWM do lado "L": com L_EN em HIGH, o M- segue este pino (velocidade para o outro lado)',
    'R_EN': 'Liga a meia ponte do M+ (HIGH). Em LOW ela desliga',
    'L_EN': 'Liga a meia ponte do M- (HIGH). Em LOW ela desliga',
    'R_IS': 'Sensor de corrente do lado R (saida analogica)', 'L_IS': 'Sensor de corrente do lado L (saida analogica)',
    'VCC': 'Alimentacao da logica: 5 V da placa', 'GND': 'GND da logica: o GND da placa (e o B- da bateria)',
    'B+': 'Bateria +, 5,5 a 27 V (motor grande: ate 43 A)', 'B-': 'Bateria -',
    'M+': 'Um fio do motor', 'M-': 'O outro fio do motor',
}
guardar('bts7960', l, a,
        [(n, 1.6, y, ['power'] if n in ('VCC', 'GND') else [], desc_bts[n]) for n, y in zip(nomes_ctl, ys_ctl)]
        + [(n, cx, cy, ['power'] if n in ('B+', 'B-') else [], desc_bts[n]) for n, (cx, cy) in zip(nomes_pot, c_bat + c_mot)])


cam = BASE + 'app/assets/pecas.json'
d = json.load(io.open(cam, encoding='utf-8'))
novos = {p['id'] for p in pecas}
d = [p for p in d if p['id'] not in novos] + pecas
d.sort(key=lambda p: p['id'])
io.open(cam, 'w', encoding='utf-8', newline='').write(json.dumps(d, ensure_ascii=False, indent=1))
print(f'\ncatalogo: {len(d)} pecas')
