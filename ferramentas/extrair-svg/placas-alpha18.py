"""python ferramentas/extrair-svg/placas-alpha18.py

As placas novas da Alpha 18.

    ESP32   : S2, S3, C3, C6 e H2
    ESP8266 : NodeMCU e os modulos ESP-01 ao ESP-14 (inclusive 12E e 12F)
    Arduino : ATtiny85 em DIP-8, separado do Franzininho
    STM32   : Nucleo-C031C6 e Nucleo-L031K6 -- BRANCAS, como o pedido diz
    Raspberry: Pi Pico W, com a logo da Raspberry (a Pico tambem ganha)

Sobre a posicao dos pinos
-------------------------
O que decide o comportamento do simulador e o NOME do pino: e ele que o
PerfilPlaca traduz em GPIO, e e por ele que o sketch liga o LED. A
posicao so decide onde o fio encosta no desenho -- mas ela e o que a
pessoa le na hora de ligar, entao vale acertar.

Os modulos ESP-01 ao ESP-14 seguem a ordem PADRAO da familia, que e a
dos diagramas da Espressif e da AI-Thinker: RST, ADC, CH_PD, GPIO16,
GPIO14, GPIO12, GPIO13, VCC de um lado, e TXD, RXD, GPIO5, GPIO4,
GPIO0, GPIO2, GPIO15, GND do outro. Cada modulo traz para fora um
subconjunto disso, nessa ordem; os de 22 pinos ganham embaixo a SPI da
flash. O ESP-01, o ESP-02 e o ESP-11 sao a excecao porque tem header
2x4 e ordem propria -- e a pinagem mais copiada do mundo, e ela fica
como e.

As Nucleo-32, o ATtiny85 em DIP-8 e as Pico tambem seguem a serigrafia
de verdade. Nas DevKit das variantes de ESP32 -- S2, S3, C3, C6 e H2 --
o arranjo muda de fabricante para fabricante e de revisao para revisao,
entao os GPIO saem em ordem numerica nas duas colunas: os nomes sao os
certos, e o que nao se faz e inventar uma serigrafia e apresenta-la
como a de um modulo especifico.
"""
import io
import json

BASE = 'C:/Users/seraf/OneDrive/Desktop/SimuladorESP32/'
PX = 96 / 25.4
PASSO = 2.54

pecas = []


# As placas refeitas na Alpha 25 tem o desenho e os pinos de outro gerador
# (placas-esp32.py, placas-arduino.py, placas-pico.py): aqui ficam de fora,
# para rodar este de novo nao trazer o desenho antigo de volta.
REFEITAS_NA_ALPHA_25 = {'esp32-s2-devkit', 'esp32-s3-devkit', 'esp32-c3-devkit', 'esp32-c6-devkit', 'esp32-h2-devkit', 'attiny85', 'pi-pico', 'pi-pico-w'}


def guardar(id_peca, svg, larg_mm, alt_mm, pinos):
    if id_peca in REFEITAS_NA_ALPHA_25:
        print(f'{id_peca}: refeita na Alpha 25, fica como esta')
        return
    io.open(BASE + f'app/assets/{id_peca}.svg', 'w', encoding='utf-8', newline='').write(svg)
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


def barra(nomes, x, y0, passo=PASSO):
    return [(n, x, y0 + i * passo) for i, n in enumerate(nomes)]


def linha(nomes, y, x0, passo=PASSO):
    """Fileira HORIZONTAL, para os modulos deitados."""
    return [(n, x0 + i * passo, y) for i, n in enumerate(nomes)]


def desambiguar(pinos):
    """Nome repetido ganha sufixo: GND, GND.2, GND.3..."""
    vistos = {}
    saida = []
    for nome, x, y in pinos:
        vistos[nome] = vistos.get(nome, 0) + 1
        saida.append((nome if vistos[nome] == 1 else f'{nome}.{vistos[nome]}', x, y))
    return saida


ALIMENTACAO = ('GND', 'AGND', '3V3', '3.3V', '5V', 'VIN', 'VBUS', 'VSYS', 'VCC', 'VB',
               '3V3_EN', 'AREF', 'ADC_VREF', '+5V', '+3V3', 'VU')


def sinais(nome, i2c=()):
    base = nome.split('.')[0]
    if base in ALIMENTACAO:
        return ['power']
    return ['i2c'] if base in i2c else []


# ---------------------------------------------------------------------
#  o desenho
# ---------------------------------------------------------------------
def placa(larg, alt, cor, borda, titulo, esq, dir_, texto='#cfd8e3', extras='',
          fonte=1.35, titulo_em=None, baixo=(), fonte_titulo=2.2, titulo_em_pe=False):
    pads, rotulos = [], []
    baixo = list(baixo)

    for nome, x, y in esq + dir_ + baixo:
        pads.append(f'  <rect x="{x - 1.0:.2f}" y="{y - 1.0:.2f}" width="2.0" height="2.0" '
                    f'rx="0.35" fill="#d8b24a"/>')
        pads.append(f'  <circle cx="{x:.2f}" cy="{y:.2f}" r="0.55" fill="#14181f"/>')

    for nome, x, y in esq:
        rotulos.append(f'  <text x="{x + 1.7:.2f}" y="{y + 0.5:.2f}" fill="{texto}" '
                       f'font-size="{fonte}px" font-family="Verdana">'
                       f'{nome.split(".")[0]}</text>')

    for nome, x, y in dir_:
        rotulos.append(f'  <text x="{x - 1.7:.2f}" y="{y + 0.5:.2f}" fill="{texto}" '
                       f'font-size="{fonte}px" text-anchor="end" font-family="Verdana">'
                       f'{nome.split(".")[0]}</text>')

    # A fileira de baixo dos ESP-12E/F e do ESP-14: o rotulo sai EM PE,
    # porque deitado nao cabe entre dois pads de 2,54 mm.
    for nome, x, y in baixo:
        curto = nome.split(".")[0].replace("GPIO", "")
        rotulos.append(f'  <text x="{x:.2f}" y="{y - 1.7:.2f}" fill="{texto}" '
                       f'font-size="{fonte}px" text-anchor="middle" '
                       f'font-family="Verdana">{curto}</text>')

    ty = titulo_em if titulo_em is not None else alt / 2 + 0.6

    # O titulo em pe cabe no corredor entre as duas colunas de pinos.
    # Deitado, num modulo estreito, ele caia em cima das etiquetas --
    # que foi o defeito do ESP-12E na Alpha 18: o nome escondia o VCC.
    giro = (f' transform="rotate(-90 {larg / 2:.2f} {ty:.2f})"'
            if titulo_em_pe else '')

    return f'''<svg xmlns="http://www.w3.org/2000/svg" version="1.1"
     width="{larg}mm" height="{alt}mm" viewBox="0 0 {larg} {alt}">

  <rect x="0" y="0" width="{larg}" height="{alt}" rx="1.4" fill="{cor}"/>
  <rect x="0" y="0" width="{larg}" height="{alt}" rx="1.4"
        fill="none" stroke="{borda}" stroke-width="0.3"/>

{extras}

{chr(10).join(pads)}
{chr(10).join(rotulos)}

  <text x="{larg / 2:.2f}" y="{ty + fonte_titulo * 0.35:.2f}" fill="{texto}"
        font-size="{fonte_titulo}px" text-anchor="middle" font-family="Verdana"
        opacity="0.85"{giro}>{titulo}</text>
</svg>
'''


def chip(x, y, w, h, cor='#1b1f27'):
    return (f'  <rect x="{x}" y="{y}" width="{w}" height="{h}" rx="0.8" fill="{cor}"/>\n'
            f'  <rect x="{x}" y="{y}" width="{w}" height="{h}" rx="0.8" '
            f'fill="none" stroke="#0f1218" stroke-width="0.25"/>')


def antena(x, y, larg=6.0, alt=4.2):
    """O serpentina impressa dos modulos ESP."""
    passos = []
    passo = larg / 7
    for i in range(7):
        xi = x + i * passo
        passos.append(f'M {xi:.2f} {y:.2f} L {xi:.2f} {y + alt:.2f} '
                      f'L {xi + passo:.2f} {y + alt:.2f} L {xi + passo:.2f} {y:.2f}')
    return (f'  <path d="{" ".join(passos)}" fill="none" stroke="#c9ccd1" '
            f'stroke-width="0.35"/>')


def framboesa(cx, cy, escala=1.0):
    """
    A logo da Raspberry Pi.

    O pedido foi explicito -- "as placas Raspberry tem que ter a logo da
    raspberry" --, e ela e o jeito mais rapido de distinguir uma Pico de
    uma Pico W numa tela cheia de retangulos verdes.
    """
    bolas = [(0, 1.5), (-1.1, 0.75), (1.1, 0.75), (-1.8, -0.2), (0, -0.1),
             (1.8, -0.2), (-1.1, -1.05), (1.1, -1.05), (0, -1.7)]

    corpo = '\n'.join(
        f'  <circle cx="{cx + dx * escala:.2f}" cy="{cy - dy * escala:.2f}" '
        f'r="{0.62 * escala:.2f}" fill="#c7053d"/>'
        for dx, dy in bolas)

    folhas = (f'  <ellipse cx="{cx - 0.95 * escala:.2f}" cy="{cy - 2.7 * escala:.2f}" '
              f'rx="{1.0 * escala:.2f}" ry="{0.55 * escala:.2f}" fill="#43b02a" '
              f'transform="rotate(-35 {cx - 0.95 * escala:.2f} {cy - 2.7 * escala:.2f})"/>\n'
              f'  <ellipse cx="{cx + 0.95 * escala:.2f}" cy="{cy - 2.7 * escala:.2f}" '
              f'rx="{1.0 * escala:.2f}" ry="{0.55 * escala:.2f}" fill="#43b02a" '
              f'transform="rotate(35 {cx + 0.95 * escala:.2f} {cy - 2.7 * escala:.2f})"/>')

    return folhas + '\n' + corpo


# =====================================================================
#  1) as variantes de ESP32
# =====================================================================
#
# Placas DevKit: duas fileiras, alimentacao nas pontas e os GPIO em
# ordem numerica. Os nomes sao os da serigrafia da Espressif, que nessas
# variantes e o numero puro do GPIO -- e nao o "D<n>" da DevKit V1.
def devkit_esp32(id_peca, titulo, gpios, i2c, cor='#7a2020', borda='#521414'):
    metade = (len(gpios) + 1) // 2

    esquerda = ['3V3', 'GND', 'EN'] + [str(g) for g in gpios[:metade]]
    direita = ['5V', 'GND', 'TX', 'RX'] + [str(g) for g in gpios[metade:]]

    altura_pinos = max(len(esquerda), len(direita))
    larg = 25.4
    alt = round(3.4 + altura_pinos * PASSO, 1)

    todos = desambiguar(barra(esquerda, 1.7, 2.6) + barra(direita, larg - 1.7, 2.6))
    e, d = todos[:len(esquerda)], todos[len(esquerda):]

    extras = (chip(9.0, alt / 2 - 6.0, 7.4, 12.0)
              + '\n' + antena(9.7, alt / 2 - 11.0, 6.0, 3.4)
              + f'\n  <rect x="10.2" y="{alt - 6.0:.2f}" width="5.0" height="4.2" '
                f'rx="0.6" fill="#2a2f38"/>')

    guardar(id_peca, placa(larg, alt, cor, borda, titulo, e, d, extras=extras,
                           titulo_em=alt / 2 + 9.0),
            larg, alt,
            [(n, x, y, sinais(n, i2c), '') for n, x, y in e + d])


# Os GPIO que cada variante realmente expoe numa DevKit.
devkit_esp32('esp32-s2-devkit', 'ESP32-S2',
             [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18,
              19, 20, 21, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42],
             i2c=('8', '9'))

devkit_esp32('esp32-s3-devkit', 'ESP32-S3',
             [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18,
              19, 20, 21, 35, 36, 37, 38, 39, 40, 41, 42, 45, 46, 47, 48],
             i2c=('8', '9'))

devkit_esp32('esp32-c3-devkit', 'ESP32-C3',
             [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 18, 19],
             i2c=('8', '9'), cor='#2b3a52', borda='#1c2737')

devkit_esp32('esp32-c6-devkit', 'ESP32-C6',
             [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18,
              19, 20, 21, 22, 23],
             i2c=('6', '7'), cor='#2b3a52', borda='#1c2737')

devkit_esp32('esp32-h2-devkit', 'ESP32-H2',
             [0, 1, 2, 3, 4, 5, 8, 9, 10, 11, 12, 13, 14, 22, 23, 24, 25, 26, 27],
             i2c=('1', '2'), cor='#3a2b52', borda='#271c37')


# =====================================================================
#  2) ESP8266
# =====================================================================
#
# NodeMCU: os nomes D0..D8 sao os da placa, e nao os do chip. E a
# confusao classica da aula -- D4 e o GPIO2 --, e por isso os dois
# aparecem no desenho.
nodemcu_esq = ['A0', 'RSV', 'RSV', 'SD3', 'SD2', 'SD1', 'CMD', 'SD0', 'CLK',
               'GND', '3V3', 'EN', 'RST', 'GND', 'VIN']
nodemcu_dir = ['D0', 'D1', 'D2', 'D3', 'D4', '3V3', 'GND', 'D5', 'D6', 'D7',
               'D8', 'RX', 'TX', 'GND', '3V3']

lnode, anode = 25.4, 48.3
todos = desambiguar(barra(nodemcu_esq, 1.7, 2.6) + barra(nodemcu_dir, lnode - 1.7, 2.6))
ne, nd = todos[:len(nodemcu_esq)], todos[len(nodemcu_esq):]

extras_node = (chip(8.6, 14.0, 8.2, 16.0)
               + '\n' + antena(9.4, 8.6, 6.4, 3.6)
               + f'\n  <rect x="9.4" y="{anode - 7.0:.2f}" width="6.6" height="5.4" '
                 f'rx="0.6" fill="#2a2f38"/>')

guardar('esp8266-nodemcu',
        placa(lnode, anode, '#1f4f6b', '#143a50', 'NodeMCU', ne, nd,
              extras=extras_node, titulo_em=34.0),
        lnode, anode,
        [(n, x, y, sinais(n, ('D1', 'D2')), '') for n, x, y in ne + nd])


# ---- os modulos ESP-xx: a ordem padrao da Espressif ----
#
# A familia inteira segue a MESMA ordem de pinos, e e ela que os
# diagramas da Espressif e da AI-Thinker mostram:
#
#     lado A (de cima para baixo):
#         RST, ADC, CH_PD, GPIO16, GPIO14, GPIO12, GPIO13, VCC
#     lado B (de cima para baixo):
#         TXD, RXD, GPIO5, GPIO4, GPIO0, GPIO2, GPIO15, GND
#
# Cada modulo traz para fora um SUBCONJUNTO disso, nessa ordem -- e a
# diferenca entre um ESP-12 e um ESP-08 e so quantos pinos couberam. Os
# de 22 pinos ganham ainda a fileira de baixo, com a flash.
#
# O ESP-01, o ESP-02 e o ESP-11 sao a excecao, e por um motivo fisico:
# eles tem duas fileiras de quatro num header de 2x4, e a ordem deles e
# a propria -- GND, GPIO2, GPIO0, RXD de um lado e TXD, CH_PD, RST, VCC
# do outro. E a pinagem mais copiada do mundo, e mexer nela por
# consistencia seria trocar o certo pelo arrumadinho.
LADO_A = ['RST', 'ADC', 'CH_PD', 'GPIO16', 'GPIO14', 'GPIO12', 'GPIO13', 'VCC']
LADO_B = ['TXD', 'RXD', 'GPIO5', 'GPIO4', 'GPIO0', 'GPIO2', 'GPIO15', 'GND']

# A fileira de baixo dos de 22 pinos: e a SPI da flash.
FLASH = ['GPIO9', 'GPIO10', 'GPIO11', 'GPIO6', 'GPIO7', 'GPIO8']

# O header 2x4 do ESP-01 e dos irmaos dele.
OITO_A = ['GND', 'GPIO2', 'GPIO0', 'RXD']
OITO_B = ['TXD', 'CH_PD', 'RST', 'VCC']


def sem(lista, fora):
    """A ordem padrao, menos os pinos que este modulo nao traz para fora."""
    return [n for n in lista if n not in fora]


MODULOS_ESP = [
    # id, titulo, lado A, lado B, fileira de baixo, antena
    ('esp-01', 'ESP-01', OITO_A, OITO_B, [], 'trilha'),
    ('esp-02', 'ESP-02', OITO_A, OITO_B, [], 'conector'),

    # 14 pinos: sai o ADC e sai o GPIO15.
    ('esp-03', 'ESP-03', sem(LADO_A, ['ADC']), sem(LADO_B, ['GPIO15']), [], 'ceramica'),
    ('esp-04', 'ESP-04', sem(LADO_A, ['ADC']), sem(LADO_B, ['GPIO15']), [], 'nenhuma'),

    # 5 pinos: so a serial. Nestes nao ha GPIO nenhum para fora, e o
    # unico jeito de piscar alguma coisa e usar o proprio TXD.
    ('esp-05', 'ESP-05', ['RST', 'VCC', 'GND'], ['TXD', 'RXD'], [], 'conector'),

    # 12 pinos: sai o ADC, o RST, o GPIO15 e o GPIO2.
    ('esp-06', 'ESP-06', sem(LADO_A, ['ADC', 'RST']),
     sem(LADO_B, ['GPIO15', 'GPIO2']), [], 'nenhuma'),

    # 16 pinos: a ordem padrao inteira.
    ('esp-07', 'ESP-07', LADO_A, LADO_B, [], 'ceramica'),

    ('esp-08', 'ESP-08', sem(LADO_A, ['ADC']), sem(LADO_B, ['GPIO15']), [], 'nenhuma'),

    ('esp-09', 'ESP-09', sem(LADO_A, ['ADC', 'RST']),
     sem(LADO_B, ['GPIO15', 'GPIO2']), [], 'ceramica'),

    ('esp-10', 'ESP-10', ['RST', 'VCC', 'GND'], ['TXD', 'RXD'], [], 'nenhuma'),

    ('esp-11', 'ESP-11', OITO_A, OITO_B, [], 'ceramica'),

    ('esp-12', 'ESP-12', LADO_A, LADO_B, [], 'trilha'),

    # 22 pinos: a ordem padrao mais a SPI da flash embaixo.
    ('esp-12e', 'ESP-12E', LADO_A, LADO_B, FLASH, 'trilha'),
    ('esp-12f', 'ESP-12F', LADO_A, LADO_B, FLASH, 'trilha'),

    # 18 pinos: a ordem padrao mais dois da flash.
    ('esp-13', 'ESP-13', LADO_A + ['GPIO9'], LADO_B + ['GPIO10'], [], 'trilha'),

    # O ESP-14 leva um STM8S003 junto do ESP8266. Os oito de cada lado
    # sao os do ESP8266, que e quem o sketch programa; embaixo saem os
    # pinos do STM8, que aqui ficam so como pontos de ligacao.
    ('esp-14', 'ESP-14', LADO_A, LADO_B,
     ['PD3', 'PC7', 'PC6', 'PD2', 'PA3', 'PD1'], 'trilha'),
]

def modulo_esp(id_peca, titulo, esquerda, direita, embaixo, tipo_antena):
    """
    Um modulo ESP-xx, do jeito que ele e de verdade: a antena numa ponta,
    os pinos dos dois lados, e -- nos de 22 pinos -- a fileira da flash
    na outra ponta.

    A Alpha 18 desenhou a antena e o nome EMBAIXO, onde ficam a fileira
    da flash e os ultimos pinos: o nome escondia o VCC e o GND, e a
    antena passava por cima dos numeros da fileira de baixo. Aqui cada
    coisa tem o seu lugar e nada cobre etiqueta nenhuma.
    """
    larg = 18.0                      # 2 mm a mais que o real: as etiquetas
                                     # das duas colunas nao se encostam
    antena_ate = 6.0                 # a zona da antena, no topo
    y0 = antena_ate + 1.4
    n = max(len(esquerda), len(direita))
    ultimo = y0 + (n - 1) * PASSO
    alt = round(ultimo + (6.2 if embaixo else 2.6), 1)

    if embaixo:
        passo_baixo = (larg - 3.2) / max(1, len(embaixo) - 1)
        pinos_baixo = linha(embaixo, alt - 1.6, 1.6, passo_baixo)
    else:
        pinos_baixo = []

    todos = desambiguar(barra(esquerda, 1.6, y0)
                        + barra(direita, larg - 1.6, y0)
                        + pinos_baixo)

    corte = len(esquerda)
    e = todos[:corte]
    d = todos[corte:corte + len(direita)]
    b = todos[corte + len(direita):]

    # A antena mora na ponta de cima, fora da area dos pinos.
    if tipo_antena == 'ceramica':
        marca = (f'  <rect x="{larg / 2 - 3.0:.2f}" y="1.4" width="6.0" height="3.0" '
                 f'rx="0.4" fill="#e8e4d8"/>')
    elif tipo_antena == 'conector':
        marca = (f'  <circle cx="{larg / 2:.2f}" cy="3.0" r="1.5" '
                 f'fill="none" stroke="#c9ccd1" stroke-width="0.5"/>')
    elif tipo_antena == 'nenhuma':
        marca = ''
    else:
        marca = antena(larg / 2 - 4.0, 1.2, 8.0, 3.2)

    separador = (f'  <line x1="1.0" y1="{antena_ate:.2f}" x2="{larg - 1.0:.2f}" '
                 f'y2="{antena_ate:.2f}" stroke="#084a4d" stroke-width="0.25"/>')

    # A blindagem metalica fica no corredor do meio, entre as etiquetas,
    # e o nome vai em pe em cima dela.
    blindagem_x = larg / 2 - 1.9
    blindagem = (f'  <rect x="{blindagem_x:.2f}" y="{y0 - 0.6:.2f}" width="3.8" '
                 f'height="{max(3.0, ultimo - y0 + 1.2):.2f}" rx="0.5" fill="#9aa3ad" '
                 f'opacity="0.55"/>')

    extras = '\n'.join(x for x in (marca, separador, blindagem) if x)

    titulo_y = (y0 + ultimo) / 2

    guardar(id_peca,
            placa(larg, alt, '#0d6b6f', '#084a4d', titulo, e, d,
                  extras=extras, fonte=1.15, titulo_em=titulo_y, baixo=b,
                  fonte_titulo=1.6, titulo_em_pe=True, texto='#e6eef2'),
            larg, alt,
            [(n, x, y, sinais(n, ('GPIO4', 'GPIO5')), '') for n, x, y in e + d + b])


for id_peca, titulo, esquerda, direita, embaixo, tipo_antena in MODULOS_ESP:
    modulo_esp(id_peca, titulo, esquerda, direita, embaixo, tipo_antena)


# =====================================================================
#  3) ATtiny85 em DIP-8
# =====================================================================
#
# Aqui a serigrafia e a do encapsulamento, e ela e fixa: pino 1 no
# canto da marca, numeracao anti-horaria. O Franzininho e uma PLACA com
# este chip dentro; ter o chip separado era o pedido.
lt, at = 10.2, 12.0
esq_t = ['PB5', 'PB3', 'PB4', 'GND']
dir_t = ['PB2', 'PB1', 'PB0', 'VCC']

# O DIP tem o pino 1 em cima a esquerda e o 8 em cima a direita: a
# coluna da direita sobe, e nao desce.
tiny_e = barra(esq_t, 1.5, 2.6)
tiny_d = list(reversed(barra(list(reversed(dir_t)), lt - 1.5, 2.6)))
todos_t = desambiguar(tiny_e + tiny_d)
te, td = todos_t[:4], todos_t[4:]

extras_t = (f'  <path d="M {lt / 2 - 1.6:.2f} 0.3 A 1.6 1.6 0 0 0 {lt / 2 + 1.6:.2f} 0.3" '
            f'fill="none" stroke="#3a3f48" stroke-width="0.3"/>\n'
            f'  <circle cx="2.6" cy="2.0" r="0.5" fill="#3a3f48"/>')

guardar('attiny85',
        placa(lt, at, '#23272e', '#14171c', 'ATtiny85', te, td,
              extras=extras_t, fonte=1.2, titulo_em=at / 2 + 0.6),
        lt, at,
        [(n, x, y, sinais(n, ('PB0', 'PB2')), '') for n, x, y in te + td])


# =====================================================================
#  4) ST Nucleo -- BRANCAS
# =====================================================================
#
# "As placas STM tem que ser brancas": as Nucleo sao mesmo, e ate a
# Alpha 17 a unica STM32 daqui era a Blue Pill, que e azul. Com fundo
# branco o texto tem que escurecer, senao o rotulo some.
BRANCO, BORDA_BRANCA, TEXTO_ESCURO = '#f2f3f5', '#c8ccd2', '#2a2f38'

# Nucleo-32: cabecalho do Arduino Nano, 15 de cada lado.
nl_esq = ['VIN', 'GND', 'RST', '5V', 'A7', 'A6', 'A5', 'A4', 'A3', 'A2',
          'A1', 'A0', 'AREF', '3V3', 'D13']
nl_dir = ['D12', 'D11', 'D10', 'D9', 'D8', 'D7', 'D6', 'D5', 'D4', 'D3',
          'D2', 'GND', 'RST', 'D0', 'D1']

lnl, anl = 18.0, 50.0
todos = desambiguar(barra(nl_esq, 1.6, 3.0) + barra(nl_dir, lnl - 1.6, 3.0))
le, ld = todos[:15], todos[15:]

extras_nl = (chip(6.0, 22.0, 6.0, 6.0, '#3a3f48')
             + f'\n  <rect x="5.6" y="2.4" width="6.8" height="7.6" rx="0.7" fill="#d8dade"/>'
             + f'\n  <circle cx="5.0" cy="{anl - 5.0:.2f}" r="0.8" fill="#43b02a"/>'
             + f'\n  <circle cx="{lnl - 5.0:.2f}" cy="{anl - 5.0:.2f}" r="0.8" fill="#2d6fd8"/>')

guardar('st-nucleo-l031k6',
        placa(lnl, anl, BRANCO, BORDA_BRANCA, 'L031K6', le, ld,
              texto=TEXTO_ESCURO, extras=extras_nl, fonte=1.2, titulo_em=33.0),
        lnl, anl,
        [(n, x, y, sinais(n, ('A4', 'A5')), '') for n, x, y in le + ld])

# Nucleo-64: cabecalho do Arduino Uno.
nc_esq = ['NC', 'IOREF', 'RST', '3V3', '5V', 'GND', 'GND', 'VIN',
          'A0', 'A1', 'A2', 'A3', 'A4', 'A5']
nc_dir = ['D15', 'D14', 'AREF', 'GND', 'D13', 'D12', 'D11', 'D10', 'D9',
          'D8', 'D7', 'D6', 'D5', 'D4']

lnc, anc = 32.0, 46.0
todos = desambiguar(barra(nc_esq, 1.8, 3.2) + barra(nc_dir, lnc - 1.8, 3.2))
ce, cd = todos[:14], todos[14:]

extras_nc = (chip(11.0, 20.0, 9.0, 9.0, '#3a3f48')
             + f'\n  <rect x="11.0" y="2.6" width="10.0" height="9.0" rx="0.8" fill="#d8dade"/>'
             + f'\n  <circle cx="6.0" cy="{anc - 4.5:.2f}" r="0.9" fill="#43b02a"/>'
             + f'\n  <circle cx="{lnc - 6.0:.2f}" cy="{anc - 4.5:.2f}" r="0.9" fill="#2d6fd8"/>')

guardar('st-nucleo-c031c6',
        placa(lnc, anc, BRANCO, BORDA_BRANCA, 'C031C6', ce, cd,
              texto=TEXTO_ESCURO, extras=extras_nc, fonte=1.2, titulo_em=34.5),
        lnc, anc,
        [(n, x, y, sinais(n, ('D14', 'D15')), '') for n, x, y in ce + cd])


# =====================================================================
#  5) Raspberry Pi Pico e Pico W -- com a logo
# =====================================================================
esq_pico = ['GP0', 'GP1', 'GND', 'GP2', 'GP3', 'GP4', 'GP5', 'GND', 'GP6', 'GP7',
            'GP8', 'GP9', 'GND', 'GP10', 'GP11', 'GP12', 'GP13', 'GND', 'GP14', 'GP15']

dir_pico = ['VBUS', 'VSYS', 'GND', '3V3_EN', '3V3', 'ADC_VREF', 'GP28', 'AGND', 'GP27',
            'GP26', 'RUN', 'GP22', 'GND', 'GP21', 'GP20', 'GP19', 'GP18', 'GND',
            'GP17', 'GP16']

lpico, apico = 21.0, 51.0

for id_peca, titulo, cor, borda in [
        ('pi-pico', 'Pi Pico', '#0f6b4f', '#0a4c38'),
        ('pi-pico-w', 'Pi Pico W', '#0f6b4f', '#0a4c38')]:

    todos = desambiguar(barra(esq_pico, 1.6, 1.7) + barra(dir_pico, lpico - 1.6, 1.7))
    pe, pd = todos[:20], todos[20:]

    # A W tem o modulo de radio no lugar em que a Pico nao tem nada. Ele
    # vai estreito e SEM o nome escrito: na Alpha 18 o "CYW43439" caia em
    # cima da etiqueta 3V3_EN, e as duas ficavam ilegiveis.
    radio = ('' if id_peca == 'pi-pico' else
             f'\n  <rect x="8.2" y="8.6" width="4.0" height="5.0" rx="0.5" fill="#b9bec6"/>'
             f'\n  <rect x="8.2" y="8.6" width="4.0" height="5.0" rx="0.5" '
             f'fill="none" stroke="#7d848e" stroke-width="0.25"/>')

    extras = (chip(6.0, 18.0, 9.0, 9.0)
              + f'\n  <rect x="7.4" y="2.0" width="6.2" height="5.4" rx="0.7" fill="#c9ccd1"/>'
              + f'\n  <rect x="6.6" y="30.0" width="7.8" height="7.0" rx="0.6" fill="#2a2f38"/>'
              + radio
              + '\n' + framboesa(lpico / 2, 44.0, 0.85))

    # O nome vai em cima do RP2040, onde as etiquetas dos lados sao
    # curtas (GP6 e GP27). Na Alpha 18 ele ficava na altura do ADC_VREF,
    # que e a etiqueta mais comprida da placa, e as duas se encavalavam.
    guardar(id_peca, placa(lpico, apico, cor, borda, titulo, pe, pd,
                           extras=extras, titulo_em=22.5, fonte_titulo=1.4),
            lpico, apico,
            [(n, x, y, sinais(n, ('GP4', 'GP5')), '') for n, x, y in pe + pd])


cam = BASE + 'app/assets/pecas.json'
d = json.load(io.open(cam, encoding='utf-8'))
novos = {p['id'] for p in pecas}
d = [p for p in d if p['id'] not in novos] + pecas
d.sort(key=lambda p: p['id'])
io.open(cam, 'w', encoding='utf-8', newline='').write(json.dumps(d, ensure_ascii=False, indent=1))

print(f'\n{len(pecas)} placas novas; catalogo com {len(d)} pecas')
