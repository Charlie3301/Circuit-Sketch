"""python ferramentas/extrair-svg/placas-alpha17.py

As duas placas do item 12 da lista do professor:

  - pi-pico  : Raspberry Pi Pico, 40 pinos, RP2040.
  - stm32f103: a "Blue Pill", que e a STM32 que a turma compra.

As duas seguem a mesma regra das outras: caixa em milimetros do modulo
de verdade, pinos no passo de 2,54 e nomes iguais aos da serigrafia --
e pela serigrafia que o aluno liga o fio.
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
REFEITAS_NA_ALPHA_25 = {'pi-pico'}


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


def barra(nomes, x, y0, passo):
    """Uma fileira vertical de pinos, do jeito que a placa fica em pe."""
    return [(n, x, y0 + i * passo) for i, n in enumerate(nomes)]


def desambiguar(pinos):
    """
    Nome repetido ganha sufixo: GND, GND.1, GND.2...

    O netlist guarda cada no como "peca:pino", entao dois pinos com o
    mesmo nome seriam o MESMO no -- o que ate e verdade para os GND de
    uma placa, mas faz o fio sumir do desenho, porque ele passa a
    apontar para um ponto so. As outras placas ja usam esse sufixo, e o
    PerfilPlaca.Base() o descarta na hora de virar numero.
    """
    vistos = {}
    saida = []
    for nome, x, y in pinos:
        vistos[nome] = vistos.get(nome, 0) + 1
        saida.append((nome if vistos[nome] == 1 else f'{nome}.{vistos[nome]}', x, y))
    return saida


def desenho_de_placa(larg, alt, cor, corBorda, titulo, esq, dir_, corTexto='#cfd8e3',
                     extras=''):
    pads = []
    for nome, x, y in esq + dir_:
        pads.append(f'  <rect x="{x - 1.0:.2f}" y="{y - 1.0:.2f}" width="2.0" height="2.0" '
                    f'rx="0.35" fill="#d8b24a"/>')
        pads.append(f'  <circle cx="{x:.2f}" cy="{y:.2f}" r="0.55" fill="#14181f"/>')

    rotulos = []
    for nome, x, y in esq:
        rotulos.append(f'  <text x="{x + 1.7:.2f}" y="{y + 0.55:.2f}" fill="{corTexto}" '
                       f'font-size="1.35px" font-family="Verdana">{nome.split(".")[0]}</text>')
    for nome, x, y in dir_:
        rotulos.append(f'  <text x="{x - 1.7:.2f}" y="{y + 0.55:.2f}" fill="{corTexto}" '
                       f'font-size="1.35px" text-anchor="end" font-family="Verdana">{nome.split(".")[0]}</text>')

    return f'''<svg xmlns="http://www.w3.org/2000/svg" version="1.1"
     width="{larg}mm" height="{alt}mm" viewBox="0 0 {larg} {alt}">

  <rect x="0" y="0" width="{larg}" height="{alt}" rx="1.4" fill="{cor}"/>
  <rect x="0" y="0" width="{larg}" height="{alt}" rx="1.4"
        fill="none" stroke="{corBorda}" stroke-width="0.3"/>

{extras}

{chr(10).join(pads)}
{chr(10).join(rotulos)}

  <text x="{larg / 2:.2f}" y="{alt / 2 + 0.6:.2f}" fill="{corTexto}" font-size="2.2px"
        text-anchor="middle" font-family="Verdana" opacity="0.85">{titulo}</text>
</svg>
'''


# =====================================================================
#  Raspberry Pi Pico
# =====================================================================
#
# 21 x 51 mm, 20 pinos de cada lado no passo de 2,54.
lpico, apico = 21.0, 51.0
y0pico = 1.7

esq_pico = ['GP0', 'GP1', 'GND', 'GP2', 'GP3', 'GP4', 'GP5', 'GND', 'GP6', 'GP7',
            'GP8', 'GP9', 'GND', 'GP10', 'GP11', 'GP12', 'GP13', 'GND', 'GP14', 'GP15']

dir_pico = ['VBUS', 'VSYS', 'GND', '3V3_EN', '3V3', 'ADC_VREF', 'GP28', 'AGND', 'GP27', 'GP26',
            'RUN', 'GP22', 'GND', 'GP21', 'GP20', 'GP19', 'GP18', 'GND', 'GP17', 'GP16']

pe, pd = desambiguar(
    barra(esq_pico, 1.6, y0pico, PASSO)
    + barra(dir_pico, lpico - 1.6, y0pico, PASSO))[:20], None
todos_pico = desambiguar(
    barra(esq_pico, 1.6, y0pico, PASSO)
    + barra(dir_pico, lpico - 1.6, y0pico, PASSO))
pe, pd = todos_pico[:20], todos_pico[20:]

extras_pico = f'''  <rect x="6.0" y="18.0" width="9.0" height="9.0" rx="0.8" fill="#1b1f27"/>
  <rect x="6.0" y="18.0" width="9.0" height="9.0" rx="0.8"
        fill="none" stroke="#0f1218" stroke-width="0.25"/>
  <rect x="7.4" y="2.0" width="6.2" height="5.4" rx="0.7" fill="#c9ccd1"/>
  <rect x="6.6" y="30.0" width="7.8" height="7.0" rx="0.6" fill="#2a2f38"/>
  <circle cx="{lpico / 2:.2f}" cy="43.5" r="1.6" fill="#1b1f27"/>'''

guardar('pi-pico', desenho_de_placa(lpico, apico, '#0f6b4f', '#0a4c38',
                                    'Pi Pico', pe, pd, extras=extras_pico),
        lpico, apico,
        [(n, x, y,
          (['power'] if n.split('.')[0] in ('GND', 'AGND', '3V3', 'VBUS', 'VSYS', '3V3_EN')
           else ['i2c'] if n in ('GP4', 'GP5') else []), '')
         for n, x, y in pe + pd])


# =====================================================================
#  STM32F103C8 "Blue Pill"
# =====================================================================
#
# 22,5 x 53 mm, 20 pinos de cada lado.
lblue, ablue = 22.5, 53.0
y0blue = 2.2

esq_blue = ['VB', 'PC13', 'PC14', 'PC15', 'PA0', 'PA1', 'PA2', 'PA3', 'PA4', 'PA5',
            'PA6', 'PA7', 'PB0', 'PB1', 'PB10', 'PB11', 'RST', '3V3', 'GND', 'GND']

dir_blue = ['3V3', 'GND', 'GND', '5V', 'PB9', 'PB8', 'PB7', 'PB6', 'PB5', 'PB4',
            'PB3', 'PA15', 'PA12', 'PA11', 'PA10', 'PA9', 'PA8', 'PB15', 'PB14', 'PB13']

todos_blue = desambiguar(
    barra(esq_blue, 1.7, y0blue, PASSO)
    + barra(dir_blue, lblue - 1.7, y0blue, PASSO))
be, bd = todos_blue[:20], todos_blue[20:]

extras_blue = f'''  <rect x="6.4" y="20.0" width="9.6" height="9.6" rx="0.8" fill="#15181e"/>
  <rect x="6.4" y="20.0" width="9.6" height="9.6" rx="0.8"
        fill="none" stroke="#0c0e12" stroke-width="0.25"/>
  <rect x="8.0" y="2.2" width="6.5" height="5.6" rx="0.7" fill="#c9ccd1"/>
  <rect x="7.2" y="33.0" width="8.2" height="4.2" rx="0.5" fill="#2a2f38"/>
  <circle cx="8.4" cy="41.5" r="0.85" fill="#3fa35c"/>
  <circle cx="{lblue - 8.4:.2f}" cy="41.5" r="0.85" fill="#d05a5a"/>'''

guardar('stm32-bluepill',
        desenho_de_placa(lblue, ablue, '#1f4fa8', '#153a7d', 'STM32F103', be, bd,
                         extras=extras_blue),
        lblue, ablue,
        [(n, x, y,
          (['power'] if n.split('.')[0] in ('GND', '3V3', '5V', 'VB')
           else ['i2c'] if n in ('PB6', 'PB7') else []), '')
         for n, x, y in be + bd])


cam = BASE + 'app/assets/pecas.json'
d = json.load(io.open(cam, encoding='utf-8'))
novos = {p['id'] for p in pecas}
d = [p for p in d if p['id'] not in novos] + pecas
d.sort(key=lambda p: p['id'])
io.open(cam, 'w', encoding='utf-8', newline='').write(json.dumps(d, ensure_ascii=False, indent=1))

print(f'\ncatalogo: {len(d)} pecas')
